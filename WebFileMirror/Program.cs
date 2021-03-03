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
        static WebClient _client = new WebClient();

        static void Main(string[] args)
        {
            StartCrawler();
            Console.ReadLine();
        }

        private static void StartCrawler()
        {
            var settings = ConfigurationManager.AppSettings;

            var uri = settings["Uri"];

#if DEBUG
            string page;
            const string dump = "testpage.html";
            if (File.Exists(dump))
            {
                page = File.ReadAllText(dump, Encoding.UTF8);
            }
            else
            {
                byte[] content = _client.DownloadData(uri);
                page = Encoding.UTF8.GetString(content);
                File.WriteAllText(dump, page, Encoding.UTF8);
            }
#else
            byte[] content = _client.DownloadData(uri);
            string page = Encoding.UTF8.GetString(content);
#endif

            var start = settings["Start"];
            var finish = settings["Finish"];

            int iStart = page.IndexOf(start) + start.Length;
            int iFinish = page.IndexOf(finish);
            page = page.Substring(iStart, iFinish - iStart);

            var mirror = settings["Mirror"];

            var hrefs = new Regex(@"<a .*href=""(doc\?number=\d*)"">(.*)<\/a>");

            foreach (Match m in hrefs.Matches(page))
            {
                var url = m.Groups[1].ToString();
                var text = m.Groups[2].ToString();

                Console.WriteLine(text);
                string path = Path.Combine(mirror, text);
                if (Directory.Exists(path))
                {
                    Directory.CreateDirectory(Path.Combine(mirror, text));
                }
            }
        }
    }
}
