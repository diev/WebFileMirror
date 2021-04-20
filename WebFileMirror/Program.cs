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
            /* HTML http://www.cbr.ru/explan/pcod/?tab.current=t2
            <div class="rubric_horizontal">
                <div class="col-md-3 rubric_sub">1&nbsp;запрос</div>
                <a class="col-md-19 offset-md-1 rubric_title" href="doc?number=40">Вопросы по Инструкции № 136-И</a>
            </div> */

            _client.BaseAddress = uri;
            _client.Encoding = Encoding.UTF8;
            string page = GetPage(uri);

            string start = "<h2 class=\"h3\">КО</h2>";
            string finish = "<h2 class=\"h3\">НФО</h2>";

            string section = GetSection(uri, page, start, finish);

            string pattern = @"<a .*href=""(?<url>doc\?number=\d*)"">(?<title>.*)<\/a>";
            var hrefs = new Regex(pattern);
            var matches = hrefs.Matches(section);

            if (matches.Count == 0)
            {
                throw new Exception($"HTML {uri} изменился - найдено 0 линков.");
            }

            foreach (Match m in matches)
            {
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
            /* HTML http://www.cbr.ru/explan/pcod/doc?number=40
            <div class="dropdown question">...</div> */

            string page = GetPage(uri);

            string start = "<div class=\"dropdown question\">";
            string finish = "<div class=\"page-info\">";

            string section = GetSection(uri, page, start, finish);
            int iStart = 0;

            bool isNext = true;
            while (isNext)
            {
                int iFinish = section.IndexOf(start, iStart + start.Length);
                if (iFinish == -1)
                {
                    isNext = false;
                    iFinish = section.Length - 1;
                }

                string subSection = section.Substring(iStart, iFinish - iStart);
                iStart = iFinish;

                Crawler3(uri, subSection, path);
            }
        }

        private static void Crawler3(string uri, string section, string path)
        {
            /* HTML http://www.cbr.ru/explan/pcod/doc?number=40
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
            string title;
            DateTime updated;

            string url = "http://www.cbr.ru";
            string urlQ = null;
            string urlA = null;

            string pattern = @"<div class=""question_num"">(?<num>.+)<\/div>";
            Match m = new Regex(pattern).Match(section);
            if (m.Success)
            {
                num = m.Result("${num}");
            }
            else
            {
                throw new Exception($"HTML {uri} изменился - не определить номер вопроса.");
            }

            pattern = @"<div class=""question_title"">(?<title>.+)<\/div>";
            m = new Regex(pattern).Match(section);
            if (m.Success)
            {
                title = m.Result("${title}");
            }
            else
            {
                throw new Exception($"HTML {uri} изменился - не определить название вопроса {num}.");
            }

            pattern = @"Дата последнего обновления: (?<date>\d{2}\.\d{2}\.\d{4})";
            m = new Regex(pattern).Match(section);
            if (m.Success)
            {
                updated = DateTime.Parse(m.Result("${date}"));
            }
            else
            {
                throw new Exception($"HTML {uri} изменился в {num} \"{title}\" - не определить дату последнего обновления.");
            }

            path = Path.Combine(path, $"{updated:yyyy-MM-dd} {GetValidFileName(title)}");
            Directory.CreateDirectory(path);

            //pattern = @"<a .*href=""(?<url>\/Queries\/UniDbQuery\/File\/(?<folder>\d*)\/(?<file>\d*)\/)Q"">";
            pattern = @"<a .*href=""(?<url>\/Queries\/UniDbQuery\/File\/\d*\/\d*\/)Q"">";
            m = new Regex(pattern).Match(section);
            if (m.Success)
            {
                urlQ = url + m.Result("${url}") + "Q";
                StoreFile(num, title, updated, "Q", urlQ, path);
            }

            pattern = @"<a .*href=""(?<url>\/Queries\/UniDbQuery\/File\/\d*\/\d*\/)A"">";
            m = new Regex(pattern).Match(section);
            if (m.Success)
            {
                urlA = url + m.Result("${url}") + "A";
                StoreFile(num, title, updated, "A", urlA, path);
            }

            if (urlQ == null && urlA == null)
            {
                throw new Exception($"HTML {uri} изменился в {num} \"{title}\" - не определить линк(и).");
            }

            Console.WriteLine($"  {updated:yyyy-MM-dd}: {title}");
        }

        private static void StoreFile(string num, string title, DateTime updated, string QA, string uri, string path)
        {
            if (uri == null)
            {
                return;
            }

            var info = new StringBuilder();
            var headers = GetHeaders(uri);

            string file = Path.Combine(path, GetFileName(headers["Content-Disposition"]));
            string fileinfo = $"{file}.{QA}.info";
            long size = long.Parse(headers["Content-Length"]);

            /*
            25: Ответ ДФМиВК от 16.04.2018 № 12-3-5/2688 на запрос КО
            Updated: 18.12.2018
            http://www.cbr.ru/Queries/UniDbQuery/File/104594/72/Q
            */

            var fi = new FileInfo(file);
            if (!fi.Exists)
            {
                _client.DownloadFile(uri, file);

                info.AppendLine($"{num}: {title}")
                    .AppendLine($"Updated: {updated:d}")
                    .AppendLine(uri)
                    .AppendLine()
                    .Append(headers);

                File.WriteAllText(fileinfo, info.ToString());
            }
            else if (fi.Length != size) //TODO new update
            {
                int n = 1;
                do
                {
                    file = $"{Path.Combine(fi.DirectoryName, Path.GetFileNameWithoutExtension(fi.Name))} ({++n}){fi.Extension}";
                }
                while (File.Exists(file));

                _client.DownloadFile(uri, file);

                info.AppendLine()
                    .AppendLine($"-{n}-")
                    .AppendLine()
                    .AppendLine($"{num}: {title}")
                    .AppendLine($"Updated: {updated:d}")
                    .AppendLine(uri)
                    .AppendLine()
                    .Append(headers);

                File.AppendAllText(fileinfo, info.ToString());
            }
        }

        private static WebHeaderCollection GetHeaders(string uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "HEAD";

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                return response.Headers;

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
            }
        }

        private static string GetFileName(string contentDisposition)
        {
            /* inline; filename=q136i_20180416_2688.pdf; filename*=utf-8''q136i_20180416_2688.pdf */
            Match m = new Regex("filename=(?<filename>.*);").Match(contentDisposition);
            if (m.Success)
            {
                return m.Result("${filename}");
            }
            else
            {
                throw new Exception($"Сервер изменился - невозможно определить имя файла.");
            }
        }

        private static string GetPage(string uri)
        {
#if DEBUG
            string file = GetValidFileName(uri) + ".html";
            if (File.Exists(file) && File.GetCreationTime(file).Date.Equals(DateTime.Now.Date))
            {
                return File.ReadAllText(file, Encoding.UTF8);
            }
            else
            {
                string page = _client.DownloadString(uri);
                File.WriteAllText(file, page, Encoding.UTF8);

                return page;
            }
#else
            return _client.DownloadString(uri);
#endif
        }

        private static string GetSection(string uri, string page, string start, string finish)
        {
            int iStart = page.IndexOf(start);
            if (iStart == -1)
            {
                throw new Exception($"HTML страницы {uri} изменился - начало секции не найдено.");
            }

            int iFinish = page.IndexOf(finish, iStart + start.Length);
            if (iFinish == -1)
            {
                throw new Exception($"HTML страницы {uri} изменился - конец секции не найден.");
            }

            string section = page.Substring(iStart, iFinish - iStart);

            return section;
        }

        private static string GetValidFileName(string uri)
        {
            string file = uri;
            Array.ForEach(Path.GetInvalidFileNameChars(), 
                c => file = file.Replace(c, '_'));

            return file;
        }
    }
}
