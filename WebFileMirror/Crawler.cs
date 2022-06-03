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

using Lib;

using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WebFileMirror
{
    internal static class Crawler
    {
        // Microsoft Edge 102.0.1245.30
        const string _userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/102.0.5005.63 Safari/537.36 Edg/102.0.1245.30";
        const string _accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";


        // Pass1 - get href[] at https://cbr.ru/explan/pcod/?tab.current=t2
        const string _start1 = "<h2 class=\"h3\">КО</h2>";
        const string _finish1 = "<h2 class=\"h3\">НФО</h2>";

        const string _href1 = @"<a .*href=""(?<url>doc\?number=\d*)"">(?<title>.*)<\/a>";

        // Pass2 - get section[] at https://cbr.ru/explan/pcod/doc?number=40
        const string _start2 = "<div class=\"dropdown question\">";
        const string _finish2 = "<div class=\"page-info\">";

        // Pass3 - get data at https://cbr.ru/explan/pcod/doc?number=40
        const string _num3 = @"<div class=""question_num"">(?<num>.+)<\/div>";
        const string _title3 = @"<div class=""question_title"">(?<title>.+)<\/div>";
        const string _date3 = @"Дата последнего обновления: (?<date>\d{2}\.\d{2}\.\d{4})";
        const string _urlQ3 = @"<a .*href=""(?<url>\/Queries\/UniDbQuery\/File\/\d*\/\d*\/)Q"">";
        const string _urlA3 = @"<a .*href=""(?<url>\/Queries\/UniDbQuery\/File\/\d*\/\d*\/)A"">";

        // Regex
        static readonly Regex _href = new Regex(_href1, RegexOptions.Compiled);
        static readonly Regex _num = new Regex(_num3, RegexOptions.Compiled);
        static readonly Regex _title = new Regex(_title3, RegexOptions.Compiled);
        static readonly Regex _date = new Regex(_date3, RegexOptions.Compiled);
        static readonly Regex _urlQ = new Regex(_urlQ3, RegexOptions.Compiled);
        static readonly Regex _urlA = new Regex(_urlA3, RegexOptions.Compiled);

        // Retries
        const int _tries = 10;
        const int _wait = 2000;

        static readonly WebClient _client = new WebClient();
        static readonly DateTime _today = DateTime.Now.Date;

        static readonly string _subsribers;
        static string _lastError = null;

        static Crawler()
        {
            _client.Headers.Set(HttpRequestHeader.UserAgent, _userAgent);
            _client.Headers.Set(HttpRequestHeader.Accept, _accept);

            _client.Encoding = Encoding.UTF8;

            var settings = ConfigurationManager.AppSettings;
            string proxy = settings["Proxy"];

            if (!string.IsNullOrEmpty(proxy))
            {
                _client.Proxy = new WebProxy(proxy)
                {
                    //Credentials = CredentialCache.DefaultCredentials
                };
            }

            _subsribers = settings["Subscribers"];
        }

        internal static void Pass1(string uri, string path)
        {
            /* HTML https://cbr.ru/explan/pcod/?tab.current=t2
            <div class="rubric_horizontal">
                <div class="col-md-3 rubric_sub">1&nbsp;запрос</div>
                <a class="col-md-19 offset-md-1 rubric_title" href="doc?number=40">Вопросы по Инструкции № 136-И</a>
            </div> */

            _client.BaseAddress = uri;

            string page = GetPage(uri);

            if (page == null)
            {
                string msg = $"Страница {uri} не получена.";

                if (_lastError != null)
                {
                    msg += " " + _lastError;
                }

                if (_client.Proxy != null)
                {
                    msg += " Проверьте также настройки Proxy.";
                }

                throw new Exception(msg);
            }

            if (page.Contains("<title>Access Denied"))
            {
                string msg = $"Страница {uri} заблокирована на Proxy.";
                throw new Exception(msg);
            }

            string section = Helper.GetSection(uri, page, _start1, _finish1);
            var matches = _href.Matches(section);

            if (matches.Count == 0)
            {
                string msg = $"HTML {uri} изменился - найдено 0 линков.";
                throw new Exception(msg);
            }

            foreach (Match m in matches)
            {
                string url = m.Result("${url}");
                string title = m.Result("${title}");

                AppTrace.Information(title);
                string dir = Path.Combine(path, title);

                if (!Directory.Exists(dir))
                {
                    AppTrace.Verbose($"MkDir {dir}");
                    Directory.CreateDirectory(dir);
                }

                Pass2(url, dir);
            }
        }

        private static void Pass2(string uri, string path)
        {
            /* HTML https://cbr.ru/explan/pcod/doc?number=40
            <div class="dropdown question">...</div> */

            string page = GetPage(uri);

            if (page == null)
            {
                return;
            }

            string section = Helper.GetSection(uri, page, _start2, _finish2);
            int iStart = 0;
            bool isNext = true;

            while (isNext)
            {
                int iFinish = section.IndexOf(_start2, iStart + _start2.Length);

                if (iFinish == -1)
                {
                    isNext = false;
                    iFinish = section.Length - 1;
                }

                string subSection = section.Substring(iStart, iFinish - iStart);
                iStart = iFinish;

                Pass3(uri, subSection, path);
            }
        }

        private static void Pass3(string uri, string section, string path)
        {
            /* HTML https://cbr.ru/explan/pcod/doc?number=40
            <div class="dropdown question">
              <div class="dropdown_title">
                <div class="question_num">02</div>
                <div class="question_title">Ответ ДФМиВК от 09.02.2021 № 12-7-ОГ/4078 на запрос физического лица</div>
              </div>
              <div class="dropdown_content">
                <div class="dropdown_content_document">
                  <div class="icon_container">
                    <div class="icon-pdf"></div>
                  </div><a class="body-2" href="/Queries/UniDbQuery/File/104594/235/Q">
                          Вопрос
                          </a></div>
                <div class="dropdown_content_document">
                  <div class="icon_container">
                    <div class="icon-doc"></div>
                  </div><a class="body-2" href="/Queries/UniDbQuery/File/104594/235/A">
                          Ответ
                        </a></div>
                <p>
                    Дата последнего обновления: 11.02.2021</p>
              </div>
            </div> */

            string num;
            var match = _num.Match(section);

            if (match.Success)
            {
                num = match.Result("${num}");
            }
            else
            {
                string msg = $"HTML {uri} изменился - не определить номер вопроса.";
                throw new Exception(msg);
            }

            string title;
            match = _title.Match(section);

            if (match.Success)
            {
                title = match.Result("${title}");
            }
            else
            {
                string msg = $"HTML {uri} изменился - не определить название вопроса {num}.";
                throw new Exception(msg);
            }

            DateTime updated;
            match = _date.Match(section);

            if (match.Success)
            {
                updated = DateTime.Parse(match.Result("${date}"));
            }
            else
            {
                string msg = $"HTML {uri} изменился в {num} \"{title}\" - не определить дату последнего обновления.";
                throw new Exception(msg);
            }

            string file = Helper.GetValidFileName(title);
            path = Path.Combine(path, $"{updated:yyyy-MM-dd} {file}");

            AppTrace.Verbose($"MkDir {path}");
            Directory.CreateDirectory(path);

            var u = new Uri(_client.BaseAddress);
            string url = $"{u.Scheme}://{u.Host}";

            string urlQ = null;
            match = _urlQ.Match(section);

            if (match.Success)
            {
                urlQ = url + match.Result("${url}") + "Q";
                StoreFile(num, title, updated, "Q", urlQ, path);
            }

            string urlA = null;
            match = _urlA.Match(section);

            if (match.Success)
            {
                urlA = url + match.Result("${url}") + "A";
                StoreFile(num, title, updated, "A", urlA, path);
            }

            if (urlQ == null && urlA == null)
            {
                string msg = $"HTML {uri} изменился в {num} \"{title}\" - не определить линк(и).";
                throw new Exception(msg);
            }

            AppTrace.Information($"  {updated:yyyy-MM-dd}: {title}");
        }

        private static void StoreFile(string num, string title, DateTime updated, string QA, string uri, string path)
        {
            var info = new StringBuilder();
            var headers = Helper.GetHeaders(uri);

            string file = Path.Combine(path, Helper.GetFileName(headers["Content-Disposition"]));
            string fileinfo = $"{file}.{QA}.txt";
            long size = long.Parse(headers["Content-Length"]);

            /*
            25: Ответ ДФМиВК от 16.04.2018 № 12-3-5/2688 на запрос КО
            Updated: 18.12.2018
            https://cbr.ru/Queries/UniDbQuery/File/104594/72/Q
            */

            var fi = new FileInfo(file);

            if (!fi.Exists)
            {
                _client.DownloadFile(uri, file);

                info.AppendLine($"{num}: {title}")
                    .AppendLine($"Updated: {updated:d}")
                    .AppendLine(uri)
                    .AppendLine()
                    .Append(headers)
                    .AppendLine()
                    .AppendLine(file);

                string msg = info.ToString();
                AppTrace.Information($"[New] {file}");

                try
                {
                    File.WriteAllText(fileinfo, msg);
                }
                catch (Exception ex)
                {
                    string m = $"Невозможно записать файл {fileinfo}!";

                    AppTrace.Error(m);
                    Mailer.SendAlert(m + Environment.NewLine + ex.Message);
                }

                Mailer.Send(_subsribers, $"Новый ({updated:d}) {title} ({QA})", msg, file);
            }
            else if (fi.Length != size) //TODO new update
            {
                int n = 1;

                do
                {
                    string name = Path.Combine(fi.DirectoryName, Path.GetFileNameWithoutExtension(fi.Name));
                    file = $"{name} ({++n}){fi.Extension}";
                }
                while (File.Exists(file));

                if (!GetFile(uri, file))
                {
                    return;
                }

                info.AppendLine()
                    .AppendLine($"-{n}-")
                    .AppendLine()
                    .AppendLine($"{num}: {title}")
                    .AppendLine($"Updated: {updated:d}")
                    .AppendLine(uri)
                    .AppendLine()
                    .Append(headers)
                    .AppendLine()
                    .AppendLine(file);

                string msg = info.ToString();
                AppTrace.Information($"[Changed] {file}");

                try
                {
                    File.AppendAllText(fileinfo, msg);
                }
                catch (Exception ex)
                {
                    string m = $"Невозможно дописать файл {fileinfo}!";

                    AppTrace.Error(m);
                    Mailer.SendAlert(m + Environment.NewLine + ex.Message);
                }

                Mailer.Send(_subsribers, $"Изменен ({updated:d}) {title} ({QA})", msg, file);
            }
            //else
            //{
            //    AppTrace.Verbose($"{file} ok");
            //}
        }

        private static string GetPage(string uri)
        {
#if DEBUG
            string file = Helper.GetValidFileName(uri) + ".html";
            bool today = File.GetCreationTime(file).Date.Equals(_today);

            if (File.Exists(file) && today)
            {
                return File.ReadAllText(file, Encoding.UTF8);
            }
#endif

            _lastError = null;
            int tries = _tries;

            while (--tries > 0)
            {
                try
                {
                    string page = _client.DownloadString(uri);

                    if (page != null && page.Contains("</html>"))
                    {
#if DEBUG
                        File.WriteAllText(file, page, Encoding.UTF8);
#endif

                        return page;
                    }
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                }

                Console.Write('.');
                Thread.Sleep(_wait);
            }

            throw new Exception(_lastError);
        }

        private static bool GetFile(string uri, string file)
        {
            _lastError = null;
            int tries = _tries;

            while (--tries > 0)
            {
                try
                {
                    _client.DownloadFile(uri, file);

                    return File.Exists(file);
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;

                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }

                Console.Write('.');
                Thread.Sleep(_wait);
            }

            throw new Exception(_lastError);
        }
    }
}
