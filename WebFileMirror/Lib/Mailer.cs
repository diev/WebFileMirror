#region License
//------------------------------------------------------------------------------
// Copyright (c) 2022 Dmitrii Evdokimov
// Open source https://github.com/diev/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//------------------------------------------------------------------------------
#endregion

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Configuration;
using System.Net.Mail;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Lib
{
    public static class Mailer
    {
        static Mailer()
        {
            AppTrace.Verbose("Mailer start...");

            Signature = $"Вы подписаны на получение извещений из {App.Version} ({Environment.MachineName}).";

            //http://stackoverflow.com/questions/454277/how-to-enable-ssl-for-smtpclient-in-web-config
            //http://www.codeproject.com/dotnet/mysteriesofconfiguration.asp

            //<configuration>
            //  <system.net>
            //    <mailSettings>
            //      <smtp from="email@host" deliveryMethod="Network">
            //        <network defaultCredentials="false"
            //          host="smtp" port="25"
            //          userName="login" password="******"
            //        />
            //      </smtp>
            //    </mailSettings>
            //  </system.net>
            //</configuration>

            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            const string section = "system.net/mailSettings";

            if (!(config.GetSectionGroup(section) is MailSettingsSectionGroup mailSettings))
            {
                throw new ArgumentNullException(section, "No this section in App.config");
            }

            method = mailSettings.Smtp.DeliveryMethod;

            switch (method)
            {
                case SmtpDeliveryMethod.Network:
                    SmtpNetworkElement settings = mailSettings.Smtp.Network;

                    host = settings.Host;

                    if (string.IsNullOrEmpty(host))
                    {
                        throw new ArgumentNullException(host, "No smtp server Host or IP specified");
                    }

                    if (!Pinger.TryPing(host, 100))
                    {
                        throw new ArgumentException("Host not pinged", host);
                    }

                    port = settings.Port;

                    // Specify whether the SmtpClient uses Secure Sockets Layer (SSL) to encrypt the connection.
                    //bool enableSsl = port == 587 || //LiveHack due to a Miscrosoft bug prior to .Net 4 (no sense of "enableSsl=" in config)
                    //                 port == 465; // Port != 25
                    ssl = settings.EnableSsl; //Just if we use .Net 4 now

                    user = settings.UserName;
                    pass = Password.Decode(settings.Password);
                    break;

                case SmtpDeliveryMethod.PickupDirectoryFromIis:
                    throw new NotImplementedException("Импорт настроек из IIS не предусмотрен.");

                case SmtpDeliveryMethod.SpecifiedPickupDirectory:
                    SmtpSpecifiedPickupDirectoryElement pickup = mailSettings.Smtp.SpecifiedPickupDirectory;

                    path = pickup.PickupDirectoryLocation;

                    if (path.Contains("{"))
                    {
                        string format = path
                            .Replace("%Now%", "0")
                            .Replace("%App%", "1");

                        path = string.Format(format,
                            DateTime.Now,
                            Assembly.GetCallingAssembly().GetName().Name);
                    }

                    if (path.Contains("%"))
                    {
                        path = Environment.ExpandEnvironmentVariables(path);
                    }

                    IOChecks.CheckDirectory(path);
                    AppTrace.Verbose("See emails in \"{0}\".", path);
                    break;

                default:
                    throw new NotImplementedException("Такой вид настроек отправки почты не предусмотрен.");
            }
        }

        //const int Timeout = 5; // sec
        const string FilesSeparator = ",;";

        static SmtpClient Client = null;
        static readonly SmtpDeliveryMethod method;

        // SmtpDeliveryMethod.Network
        static readonly string host;
        static readonly int port;
        static readonly bool ssl;
        static readonly string user;
        static readonly string pass;

        // SmtpDeliveryMethod.SpecifiedPickupDirectory
        static readonly string path;

        static bool IsReady = true;
        static readonly ConcurrentQueue<MailMessage> Queue = new ConcurrentQueue<MailMessage>();

        /// <summary>
        /// Email to send alerts.
        /// </summary>
        public static string Admin { get; set; } = string.Empty;

        /// <summary>
        /// A constant text under your message body.
        /// </summary>
        public static string Signature { get; set; } = string.Empty;

        private static void Start()
        {
            AppTrace.Verbose("SMTP start...");

            if (ssl)
            {
                ServicePointManager.ServerCertificateValidationCallback = 
                    new System.Net.Security.RemoteCertificateValidationCallback(RemoteServerCertificateValidationCallback);
            }

            switch (method)
            {
                case SmtpDeliveryMethod.Network:
                    Client = new SmtpClient(host, port)
                    {
                        UseDefaultCredentials = false,
                        Credentials = new NetworkCredential(user, pass),
                        EnableSsl = ssl
                    };
                    // Client.Timeout = Timeout * 1000; // Ignored for Async
                    break;

                case SmtpDeliveryMethod.SpecifiedPickupDirectory:
                    Client = new SmtpClient
                    {
                        PickupDirectoryLocation = path
                    };
                    break;
            }

            // Set the method that is called back when the send operation ends.
            Client.SendCompleted += new SendCompletedEventHandler(SendCompletedCallback);
            Client.DeliveryMethod = method;
        }

        private static void Stop()
        {
            DropQueue();

            if (Client != null)
            {
                //QUIT SMTP
                Client.Dispose();
                Client = null;
                AppTrace.Verbose("SMTP stop.");
            }
        }

        private static void DropQueue(int count = 0, bool isSent = false)
        {
            if (Queue.IsEmpty)
            {
                AppTrace.Verbose("Queue is empty.");
                return;
            }

            MailMessage drop;

            if (count == 1)
            {
                Queue.TryDequeue(out drop);

                if (isSent)
                {
                    AppTrace.Verbose("[{0}] Dequeued sent.", drop.Subject);
                }
                else
                {
                    AppTrace.Warning("[{0}] Dequeued unsent.", drop.Subject);
                }

                drop.Dispose();
                return;
            }

            while (!Queue.IsEmpty)
            {
                Queue.TryDequeue(out drop);
                AppTrace.Warning("[{0}] Dropped from queue.", drop.Subject);
                drop.Dispose();
            }
        }

        /// <summary>
        /// Sends an alert e-mail message to the Admin. 
        /// </summary>
        /// <param name="msg">Sets the error message to send.</param>
        public static void SendAlert(string msg)
        {
            if (string.IsNullOrWhiteSpace(Admin))
            {
                AppTrace.Warning("Admin email is not set!");
            }
            else
            {
                Send(Admin, "Alert!", msg);
            }
        }

        /// <summary>
        /// Sends the specified e-mail message to an SMTP server for delivery. 
        /// This method does not block the calling thread.
        /// </summary>
        /// <param name="to">Sets the address collection that contains the recipients of this e-mail message separated by ','.</param>
        /// <param name="subj">Sets the subject line for this e-mail message.</param>
        /// <param name="body">Sets the message body.</param>
        /// <param name="files">Sets the attachment collection used to store data attached to this e-mail message separated by ','.</param>
        public static void Send(string to, string subj, string body = null, string files = null)
        {
            if (!string.IsNullOrWhiteSpace(to) && to.Contains("@"))
            {
                MailMessage email = new MailMessage();

                email.From = new MailAddress(email.From.Address, App.Name, Encoding.UTF8);
                email.To.Add(to.Replace(';', ','));

                // email.CC
                // email.Bcc
                // email.ReplyToList;

                email.Subject = subj;

                if (string.IsNullOrWhiteSpace(body))
                {
                    email.Body = Signature;
                }
                else
                {
                    email.Body = body + Environment.NewLine + Signature;
                }

                // email.BodyEncoding = Encoding.UTF8;
                // email.IsBodyHtml = true;
                // email.Priority = MailPriority.High;

                if (!string.IsNullOrWhiteSpace(files))
                {
                    char[] sep = FilesSeparator.ToCharArray();
                    string[] filesList = files.Split(sep, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string filesEntry in filesList)
                    {
                        string fileEntry = filesEntry.Trim();
                        string fileMask = Path.GetFileName(fileEntry);
                        DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(fileEntry));

                        foreach (FileInfo fi in di.GetFiles(fileMask))
                        {
                            Attachment attachment = new Attachment(fi.FullName);
                            ContentDisposition disposition = attachment.ContentDisposition;
                            disposition.CreationDate = fi.CreationTime;
                            disposition.ModificationDate = fi.LastWriteTime;
                            disposition.ReadDate = fi.LastAccessTime;
                            email.Attachments.Add(attachment);
                        }
                    }

                    if (email.Attachments.Count == 0)
                    {
                        AppTrace.Warning("Attachments in \"{0}\" not found!", files);
                    }
                }

                Queue.Enqueue(email);
                //AppTrace.Information("[{0}] Queued to send.", email.Subject);
                AppTrace.Verbose("[{0}] to send", email.Subject);
            }

            Delivery();
        }

        /// <summary>
        /// Tries to send a next queued message and exits if none.
        /// </summary>
        public static void Delivery()
        {
            if (!IsReady || Queue.IsEmpty)
            {
                return;
            }

            IsReady = false;

            if (Client == null)
            {
                Start();

                if (Client == null)
                {
                    AppTrace.Error("Ошибка инициализации SMTP.");
                    return;
                }
            }

            //int ms = 250; // 1/4 second to sleep

            // Create an AutoResetEvent to signal the timeout threshold in the
            // timer callback has been reached.
            //var autoEvent = new AutoResetEvent(false);
            //var statusChecker = new StatusChecker(Timeout * 1000 / ms);

            // Create a timer that invokes CheckStatus after ms and every ms thereafter.
            // Console.WriteLine("{0:h:mm:ss.fff} Creating timer.\n", DateTime.Now);
            //var stateTimer = new Timer(statusChecker.CheckStatus, autoEvent, ms, ms);

            //Console.WriteLine("Sending message... wait {0} sec or press Esc to cancel.", Timeout);

            Queue.TryPeek(out MailMessage email);

            // The userState can be any object that allows your callback 
            // method to identify this send operation.
            // For this example, the userToken is a string constant.
            string userState = email.Subject;

            try
            {
                //AppTrace.Verbose("[{0}] Sending...", email.Subject);
                Client.SendAsync(email, userState);
            }
            catch (SmtpFailedRecipientsException ex)
            {
                for (int i = 0; i < ex.InnerExceptions.Length; i++)
                {
                    SmtpStatusCode status = ex.InnerExceptions[i].StatusCode;

                    if (status == SmtpStatusCode.MailboxBusy ||
                        status == SmtpStatusCode.MailboxUnavailable ||
                        status == SmtpStatusCode.TransactionFailed)
                    {
                        AppTrace.Warning("Проблема с доступностью - повтор через 5 секунд.");
                        Thread.Sleep(5000);
                        AppTrace.Verbose("[{0}] Sending again...", email.Subject);
                        Client.SendAsync(email, userState);
                    }
                    else
                    {
                        AppTrace.Error("Отправка на {0} не состоялась: {1}",
                            ex.InnerExceptions[i].FailedRecipient, ex.InnerExceptions[i].ToString());
                    }
                }
            }
            catch (SmtpException ex)
            {
                Client.SendAsyncCancel();
                AppTrace.Error("Отправка прервана по ошибке соединения с сервером: " + ex.ToString());
            }
            //finally
            //{
            //    AppTrace.Verbose("[{0}] Disposing...", email.Subject);
            //    email.Dispose(); // not in Async mode!
            //}

            //// When autoEvent signals time is out, dispose of the timer.
            //autoEvent.WaitOne();
            //stateTimer.Dispose();
            //if (statusChecker.Canceled)
            //{
            //    Client.SendAsyncCancel();
            //    AppTrace.Warning("Отправка прервана пользователем.");
            //}
            //else if (statusChecker.TimedOut)
            //{
            //    Client.SendAsyncCancel();
            //    AppTrace.Warning("Отправка прервана по таймауту.");
            //}
        }

        /// <summary>
        /// Tries to send all queued messages with the specified timeout.
        /// </summary>
        /// <param name="wait">Minutes to try to send before dropping all remain queued messages.</param>
        public static void FinalDelivery(int wait)
        {
            DateTime dt = DateTime.Now.AddMinutes(wait);

            while (!Queue.IsEmpty)
            {
                if (DateTime.Now > dt)
                {
                    AppTrace.Error("Mail still not delivered.");
                    DropQueue();
                    break;
                }

                Thread.Sleep(1000);
                AppTrace.Verbose("Final delivery waiting...");
                Delivery();
            }

            Stop();
        }

        /// <summary>
        /// A callback routine if the email is sent or canceled.
        /// </summary>
        /// <param name="sender">The object sender.</param>
        /// <param name="e">The event arguments.</param>
        private static void SendCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            // Get the unique identifier for this asynchronous operation.
            string token = (string)e.UserState;

            if (e.Cancelled)
            {
                AppTrace.Warning("[{0}] Send canceled.", token);
                DropQueue(1);
            }
            else if (e.Error != null)
            {
                AppTrace.Warning("[{0}] {1}", token, e.Error.ToString());
                DropQueue(1);
            }
            else
            {
                AppTrace.Information("[{0}] Message sent.", token);
                DropQueue(1, true);
            }

            IsReady = true;
            Delivery();
        }

        /// <summary>
        /// A callback routine to validate the server's certificate.
        /// </summary>
        /// <param name="sender">The object sender.</param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="sslPolicyErrors"></param>
        /// <returns>The server is valid</returns>
        private static bool RemoteServerCertificateValidationCallback(object sender, 
            System.Security.Cryptography.X509Certificates.X509Certificate certificate, 
            System.Security.Cryptography.X509Certificates.X509Chain chain, 
            System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            //Console.WriteLine(certificate);
            AppTrace.Verbose(certificate.ToString());

            return true; //TODO check if valid
        }

        //class StatusChecker
        //{
        //    private int invokeCount;
        //    private int maxCount;

        //    public bool Canceled = false;
        //    public bool TimedOut = false;

        //    public StatusChecker(int count)
        //    {
        //        invokeCount = 0;
        //        maxCount = count;
        //    }

        //    // This method is called by the timer delegate.
        //    public void CheckStatus(Object stateInfo)
        //    {
        //        AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
        //        invokeCount++;
        //        // Console.WriteLine("{0:h:mm:ss.fff} Checking status {1,2}.", DateTime.Now, invokeCount);

        //        if (Console.KeyAvailable)
        //        {
        //            ConsoleKeyInfo keyInfo = new ConsoleKeyInfo();
        //            keyInfo = Console.ReadKey(true);
        //            if (keyInfo.Key == ConsoleKey.Escape)
        //            {
        //                // Reset the counter and signal the waiting thread.
        //                invokeCount = 0;
        //                Canceled = true;
        //                autoEvent.Set();
        //            }
        //        }
        //        else if (invokeCount == maxCount)
        //        {
        //            // Reset the counter and signal the waiting thread.
        //            invokeCount = 0;
        //            TimedOut = true;
        //            autoEvent.Set();
        //        }
        //    }
        //}
    }
}
