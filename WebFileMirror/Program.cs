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
using System.Diagnostics;
using System.IO;

namespace WebFileMirror
{
    internal class Program
    {
        internal static void Main(string[] args)
        {
            Console.WriteLine(App.Version);
            Console.WriteLine(App.Description);

            var settings = ConfigurationManager.AppSettings;
            Mailer.Admin = settings["Admin"];

            foreach (string arg in args)
            {
                switch (arg.ToLower())
                {
                    case "v":
                        Console.WriteLine("Verbose running...");
                        AppTrace.TraceSource.Switch.Level = SourceLevels.All;
                        break;

                    case "t":
                        Mailer.SendAlert("Test");
                        AppExit.Information("Тест почты завершен.");
                        break;
                }
            }

            var uri = settings["Uri"];
            var mirror = settings["Mirror"];

            if (!Directory.Exists(mirror))
            {
                AppTrace.Verbose($"MkDir {mirror}");
                Directory.CreateDirectory(mirror);
            }

            Console.WriteLine("Попытка соединиться...");

            try
            {
                Crawler.Pass1(uri, mirror);
            }
            catch (Exception ex)
            {
                Mailer.SendAlert(ex.Message);
                AppTrace.Error(ex.Message);
            }

            Mailer.FinalDelivery(2);

            if (settings["WaitClose"].Equals("1"))
            {
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}
