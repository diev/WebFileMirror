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
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace WebFileMirror
{
    internal static class Helper
    {
        internal static WebHeaderCollection GetHeaders(string uri)
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

        internal static int GetSectionStart(string uri, string page, string startPattern)
        {
            int i = page.IndexOf(startPattern);

            if (i == -1)
            {
                string msg = $"HTML страницы {uri} изменился - начало секции \'{startPattern}\' не найдено.";
                throw new Exception(msg);
            }

            return i + startPattern.Length;
        }
        internal static int GetSectionLength(string uri, string page, int startAt, string finishPattern)
        {
            int i = page.IndexOf(finishPattern, startAt);

            if (i == -1)
            {
                string msg = $"HTML страницы {uri} изменился - конец секции \'{finishPattern}\' не найден.";
                throw new Exception(msg);
            }

            return i - startAt;
        }

        internal static string GetSection(string uri, string page, string startPattern, string finishPattern)
        {
            int iStart = page.IndexOf(startPattern);

            if (iStart == -1)
            {
                string msg = $"HTML страницы {uri} изменился - начало секции \'{startPattern}\' не найдено.";
                throw new Exception(msg);
            }

            int iFinish = page.IndexOf(finishPattern, iStart + startPattern.Length);

            if (iFinish == -1)
            {
                string msg = $"HTML страницы {uri} изменился - конец секции \'{finishPattern}\' не найден.";
                throw new Exception(msg);
            }

            string section = page.Substring(iStart, iFinish - iStart);

            return section;
        }

        internal static string GetValidFileName(string uri)
        {
            string file = uri;

            Array.ForEach(Path.GetInvalidFileNameChars(),
                c => file = file.Replace(c, '_'));

            return file;
        }

        internal static string GetFileName(string contentDisposition)
        {
            /* inline; filename=q136i_20180416_2688.pdf; filename*=utf-8''q136i_20180416_2688.pdf */
            Match m = new Regex("filename=(?<filename>.*);").Match(contentDisposition);

            if (m.Success)
            {
                string filename = m.Result("${filename}");

                if (filename.Length > 0)
                {
                    return filename;
                }
            }

            string msg = $"Сервер изменился - невозможно определить имя файла.";
            throw new Exception(msg);
        }
    }
}
