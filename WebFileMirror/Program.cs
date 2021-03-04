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
            Console.ReadLine();
        }

        private static void Crawler1(string uri, string path)
        {
            /* HTML http://www.cbr.ru/explan/pcod/
            <div class="rubric_horizontal">
                <div class="col-md-3 rubric_sub">1&nbsp;запрос</div>
                <a class="col-md-19 offset-md-1 rubric_title" href="doc?number=40">Вопросы по Инструкции № 136-И</a>
            </div> */

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

                Crawler2(uri + url, dir);
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

            var titleR = new Regex(@"<div class=""question_title"">(?<title>.+)<\/div>");

            //var hrefQ = new Regex(@"<a .*href=""\/Queries\/UniDbQuery\/File\/(?<folder>\d*)\/(?<file>\d*)\/Q"">\s*Вопрос\s*<\/a>");
            //var hrefA = new Regex(@"<a .*href=""\/Queries\/UniDbQuery\/File\/(?<folder>\d*)\/(?<file>\d*)\/A"">\s*Ответ\s*<\/a>");
            var hrefR = new Regex(@"<a .*href=""\/Queries\/UniDbQuery\/File\/(?<folder>\d*)\/(?<file>\d*)\/Q"">\s*Вопрос\s*<\/a>");

            var dateR = new Regex(@"Дата последнего обновления: (?<date>\d{2}\.\d{2}\.\d{4})");

            Match m;

            string title;
            string folder;
            //string fileQ;
            //string fileA;
            string file;
            DateTime updated;

            m = titleR.Match(section);
            if (m.Success)
            {
                title = m.Result("${title}");
            }
            else
            {
                throw new Exception($"HTML изменился - невозможно определить название документа.");
            }

            m = hrefR.Match(section);
            if (m.Success)
            {
                folder = m.Result("${folder}");
                file = m.Result("${file}");
            }
            else
            {
                throw new Exception($"HTML изменился - невозможно определить файл вопроса.");
            }

            m = dateR.Match(section);
            if (m.Success)
            {
                updated = DateTime.Parse(m.Result("${date}"));
            }
            else
            {
                throw new Exception($"HTML изменился - невозможно определить дату последнего обновления.");
            }

            File.WriteAllText(Path.Combine(path, $"{folder}_{file}.txt"), $"Updated: {updated}");
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
