#region License
//------------------------------------------------------------------------------
// Copyright (c) Dmitrii Evdokimov
// Source https://github.com/diev/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//------------------------------------------------------------------------------
#endregion

using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace WebFileMirror
{
    class Program
    {
        static readonly WebClient _client = new WebClient();

        static void Main(string[] args)
        {
            var settings = ConfigurationManager.AppSettings;
            var uri = settings["Uri"];
            var mirror = settings["Mirror"];
            if (!Directory.Exists(mirror))
            {
                Directory.CreateDirectory(mirror);
            }

            Crawler1(uri, mirror);

            Console.WriteLine("Press Enter to exit.");
            Console.ReadLine();
        }

        private static void Crawler1(string uri, string path)
        {
            /* HTML http://www.cbr.ru/explan/pcod/
            <div class="rubric_horizontal">
                <div class="col-md-3 rubric_sub">1&nbsp;запрос</div>
                <a class="col-md-19 offset-md-1 rubric_title" href="doc?number=40">Вопросы по Инструкции № 136-И</a>
            </div> */

            _client.BaseAddress = uri;
            string page = GetPage(uri);

            string start = "<h2 class=\"h3\">КО</h2>";
            string finish = "<h2 class=\"h3\">НФО</h2>";

            int iStart = page.IndexOf(start) + start.Length;
            int iFinish = page.IndexOf(finish);
            string section = page.Substring(iStart, iFinish - iStart);

            var hrefs = new Regex(@"<a .*href=""(?<url>doc\?number=\d*)"">(?<title>.*)<\/a>");

            foreach (Match m in hrefs.Matches(section))
            {
                //var url = m.Groups[1].ToString();
                //var title = m.Groups[2].ToString();

                string url = m.Result("${url}");
                string title = m.Result("${title}");

                Console.WriteLine(title);
                string dir = Path.Combine(path, title);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Crawler2(url, dir);
            }
        }

        private static void Crawler2(string uri, string path)
        {
            /* HTML http://www.cbr.ru/explan/pcod/doc/?number=40
            <div class="dropdown question">...</div> */

            string page = GetPage(uri);

            string start = "<div class=\"dropdown question\">";
            string finish = "<div class=\"page-info\">";

            int iStart = page.IndexOf(start);
            if (iStart == -1)
            {
                throw new Exception($"HTML страницы {uri} изменился - невозможно определить секции данных.");
            }

            bool next = true;
            while (next)
            {
                iStart += start.Length;
                int iFinish = page.IndexOf(start, iStart);
                if (iFinish == -1)
                {
                    iFinish = page.IndexOf(finish, iStart);
                    if (iFinish == -1) // Last
                    {
                        iFinish = page.Length - 1;
                    }

                    next = false;
                }

                string section = page.Substring(iStart, iFinish - iStart);
                iStart = iFinish;

                Crawler3(section, path);
            }
        }

        private static void Crawler3(string section, string path)
        {
            /* HTML http://www.cbr.ru/explan/pcod/doc/?number=40
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

            string title;
            DateTime updated;

            string url;
            string folder;
            string file;

            Match m = new Regex(@"<div class=""question_title"">(?<title>.+)<\/div>").Match(section);
            if (m.Success)
            {
                title = m.Result("${title}");
            }
            else
            {
                throw new Exception($"HTML изменился - невозможно определить название документа.");
            }

            m = new Regex(@"<a .*href=""(?<url>\/Queries\/UniDbQuery\/File\/(?<folder>\d*)\/(?<file>\d*)\/)Q"">\s*Вопрос\s*<\/a>").Match(section);
            if (m.Success)
            {
                url = m.Result("${url}");
                folder = m.Result("${folder}");
                file = m.Result("${file}");
            }
            else
            {
                throw new Exception($"HTML изменился - невозможно определить файл вопроса.");
            }

            m = new Regex(@"Дата последнего обновления: (?<date>\d{2}\.\d{2}\.\d{4})").Match(section);
            if (m.Success)
            {
                updated = DateTime.Parse(m.Result("${date}"));
            }
            else
            {
                throw new Exception($"HTML изменился - невозможно определить дату последнего обновления.");
            }

            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"Updated: {updated:d}");
            sb.AppendLine();
            sb.AppendLine($"Q: {url}Q");
            sb.AppendLine($"A: {url}A");

            /*
            Ответ ДФМиВК от 16.04.2018 № 12-3-5/2688 на запрос КО
            Updated: 18.12.2018
            */

            string item = $"{folder}_{file}_{updated:yyyy-MM-dd}";
            path = Path.Combine(path, item);
            Directory.CreateDirectory(path);

            File.WriteAllText(Path.Combine(path, item + ".info"), sb.ToString());

            WriteFile(url + "Q", path, true, true);
            WriteFile(url + "A", path, true, true);
        }

        private static void WriteFile(string uri, string path, bool writeHeaders, bool writeContent)
        {
            using (var responseStream = _client.OpenRead(uri))
            {
                var responseHeaders = _client.ResponseHeaders;

                string file;
                var rexpFilename = new Regex("filename=(?<filename>.*);");
                Match m = rexpFilename.Match(responseHeaders.Get("Content-Disposition"));
                if (m.Success)
                {
                    file = Path.Combine(path, m.Result("${filename}"));
                }
                else
                {
                    throw new Exception($"Сервер изменился - невозможно определить имя файла.");
                }

                if (writeHeaders)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < responseHeaders.Count; i++)
                    {
                        sb.AppendLine($"{responseHeaders.GetKey(i)}: {responseHeaders.Get(i)}");
                    }

                    /* (Q)
                    Connection: keep-alive
                    Keep-Alive: timeout=60
                    Content-Length: 258694
                    Cache-Control: private
                    Content-Type: application/pdf
                    Date: Sat, 06 Mar 2021 15:33:34 GMT
                    Last-Modified: Tue, 18 Dec 2018 14:42:19 GMT
                    Set-Cookie: __ddg1=ZI71Ug0pN298UWQ6HY41; Domain=.cbr.ru; HttpOnly; Path=/; Expires=Sun, 06-Mar-2022 15:33:33 GMT,ASPNET_SessionID=oy3kclmvasmb1ybo5tdkz0r5; path=/; HttpOnly; SameSite=Lax
                    Server: ddos-guard
                    X-AspNetMvc-Version: 5.2
                    X-Zoom-Title: 0J7RgtCy0LXRgiDQlNCk0JzQuNCS0Jog0L7RgiAxNi4wNC4yMDE4IOKEliAxMi0zLTUvMjY4OCDQvdCwINC30LDQv9GA0L7RgSDQmtCeICjRgtC10LrRgdGCINCy0L7Qv9GA0L7RgdCwKQ==
                    Content-Disposition: inline; filename=q136i_20180416_2688.pdf; filename*=utf-8''q136i_20180416_2688.pdf
                    X-AspNet-Version: 4.0.30319
                    X-Powered-By: ASP.NET
                    */

                    File.WriteAllText(file + ".info", sb.ToString());
                }

                if (writeContent)
                { 
                    using (var fileStream = new FileStream(file, FileMode.OpenOrCreate))
                    {
                        responseStream.CopyTo(fileStream);
                        fileStream.Flush();
                    }
                }
            }
        }

        private static string GetPage(string uri)
        {
            string page;

#if DEBUG
            string file = GetFileName(uri);

            if (File.Exists(file))
            {
                page = File.ReadAllText(file, Encoding.UTF8);
            }
            else
            {
                byte[] content = _client.DownloadData(uri);
                page = Encoding.UTF8.GetString(content);
                File.WriteAllText(file, page, Encoding.UTF8);
            }
#else
            byte[] content = _client.DownloadData(uri);
            page = Encoding.UTF8.GetString(content);
#endif

            return page;
        }

        private static string GetFileName(string uri)
        {
            return uri
                .Replace('\\', '_')
                .Replace('/', '_')
                .Replace(':', '_')
                .Replace('*', '_')
                .Replace('?', '_')
                .Replace('\"', '_')
                .Replace('\'', '_')
                + ".html";
        }
    }
}
