using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;


namespace Exias_console
{
    class Program
    {
        #region settings
        public static string AnalyzerCode = "903"; //код из аналайзер конфигурейшн

        public static bool ServiceIsActive;        // флаг для запуска и остановки потока
        public static string AnalyzerResultPath = AppDomain.CurrentDomain.BaseDirectory + "\\AnalyzerResults"; // папка для файлов с результатами
        public static bool FileToErrorPath;        // флаг для перемещения файлов в ошибки или архив

        //public static string CGMPath = AppDomain.CurrentDomain.BaseDirectory + "\\CGM"; // папка для файлов с результатами для CGM
        #endregion

        #region функции логов
        #endregion

        // Функция преобразования кода теста прибора в код теста PSMV2 в CGM
        public static string TranslateToPSMCodes(string AnalyzerTestCodesPar)
        {
            switch (AnalyzerTestCodesPar)
            {
                case "K":
                    return "0025";
                case "Na":
                    return "0030";
                case "Cl":
                    return "0016";
                default:
                    return "";
            }    
        }

        #region Функция обработки файлов с результатами и создания файлов для службы, которая разберет файл и запишет данные в CGM
        static void ResultsProcessing()
        {
            while (ServiceIsActive)
            {
                try
                {
                    if (!Directory.Exists(AnalyzerResultPath))
                    {
                        Directory.CreateDirectory(AnalyzerResultPath);
                    }

                    #region папки архива, результатов и ошибок
                    // архивная папка
                    string ArchivePath = AnalyzerResultPath + @"\Archive";
                    // папка для ошибок
                    string ErrorPath = AnalyzerResultPath + @"\Error";
                    // папка для файлов с результатами для CGM
                    string CGMPath = AnalyzerResultPath + @"\CGM";

                    if (!Directory.Exists(ArchivePath))
                    {
                        Directory.CreateDirectory(ArchivePath);
                    }

                    if (!Directory.Exists(ErrorPath))
                    {
                        Directory.CreateDirectory(ErrorPath);
                    }

                    if (!Directory.Exists(CGMPath))
                    {
                        Directory.CreateDirectory(CGMPath);
                    }
                    #endregion

                    // строки для формирования файла (psm файла) с результатами для службы,
                    // которая разбирает файлы и записывает результаты в CGM
                    string MessageHead = "";
                    string MessageTest = "";
                    string AllMessage = "";

                    // поолучаем список всех файлов в текущей папке
                    string[] Files = Directory.GetFiles(AnalyzerResultPath, "*.res");

                    // шаблоны регулярных выражений для поиска данных
                    string RIDPattern = @"[O][|][1][|](?<RID>\d+)[|]{1}\S*";
                    //string ResultPattern = @"\d+R[|]\d+[|](?<Test>\S+)[|]\S*";
                    string TestPattern = @"[R][|]\d+[|][@]+(?<Test>\w+)[@]\S*";
                    string ResultPattern = @"[R][|]\d+[|]\S+[|](?<Result>\d+[.]?\d*)[|]\S+[|]\S*";

                    Regex RIDRegex = new Regex(RIDPattern, RegexOptions.None, TimeSpan.FromMilliseconds(150));
                    Regex TestRegex = new Regex(TestPattern, RegexOptions.None, TimeSpan.FromMilliseconds(150));
                    Regex ResultRegex = new Regex(ResultPattern, RegexOptions.None, TimeSpan.FromMilliseconds(150));

                    // проходим по файлам
                    foreach (string file in Files)
                    {
                        Console.WriteLine(file);
                        string[] lines = System.IO.File.ReadAllLines(file);
                        string RID = "";
                        string Test = "";
                        string Result = "";
                        // обрезаем только имя текущего файла
                        string FileName = file.Substring(AnalyzerResultPath.Length + 1);
                        // название файла .ок, который должен создаваться вместе с результирующим для обработки службой FileGetterService
                        string OkFileName = "";
                        Console.WriteLine(FileName);
                        // проходим по строкам в файле
                        foreach (string line in lines)
                        {
                            // заменяем птички на @, иначе регулярное врыажение некорректно работает
                            string line_ = line.Replace("^", "@");
                            Match RIDMatch = RIDRegex.Match(line_);
                            Match TestMatch = TestRegex.Match(line_);
                            Match ResultMatch = ResultRegex.Match(line_);

                            // поиск RID в строке
                            if (RIDMatch.Success)
                            {
                                RID = RIDMatch.Result("${RID}");
                                Console.WriteLine(RID);
                                MessageHead = $"O|1|{RID}||ALL|R|20230101000100|||||X||||ALL||||||||||F";
                            }
                            else
                            {
                                //Console.WriteLine("RID не найден в строке");
                                //FileToErrorPath = true;
                            }

                            // поиск теста в строке
                            if (TestMatch.Success)
                            {
                                Test = TestMatch.Result("${Test}");
                                // преобразуем тест в код теста PSM
                                string PSMTestCode = TranslateToPSMCodes(Test);
                                Console.WriteLine(PSMTestCode);
                                //Console.WriteLine(Test);
                                if (ResultMatch.Success)
                                {
                                    Result = ResultMatch.Result("${Result}");
                                    Console.WriteLine($"{Test} - result: {Result}");
                                }
                                
                                // если код тест был интерпретирован
                                if (PSMTestCode != "")
                                {
                                    // формируем строку с ответом для результирующего файла
                                    MessageTest = MessageTest + $"R|1|^^^{PSMTestCode}^^^^{AnalyzerCode}|{Result}|||N||F||Chaykina^||20230101000001|{AnalyzerCode}" + "\r";
                                    Console.WriteLine(MessageTest);
                                }
                            }
                        }

                        // получаем название файла .ок на основании файла с результатом
                        if (FileName.IndexOf(".") != -1)
                        {
                            OkFileName = FileName.Split('.')[0] + ".ok";
                            Console.WriteLine(OkFileName);
                        }

                        // если строки с результатами и с ШК не пустые, значит формируем результирующий файл
                          if (MessageHead != "" && MessageTest != "")
                          {
                                try
                                {
                                    // собираем полное сообщение с результатом
                                    AllMessage = MessageHead + "\r" + MessageTest;
                                    //Console.WriteLine(AllMessage);

                                    // создаем файл для записи результата в папке для рез-тов
                                    if (!File.Exists(CGMPath + @"\" + FileName))
                                {
                                    using (StreamWriter sw = File.CreateText(CGMPath + @"\" + FileName))
                                    {
                                        foreach (string msg in AllMessage.Split('\r'))
                                        {
                                            sw.WriteLine(msg);
                                        }
                                    }
                                }
                                    else
                                {
                                    File.Delete(CGMPath + @"\" + FileName);
                                    using (StreamWriter sw = File.CreateText(CGMPath + @"\" + FileName))
                                    {
                                        foreach (string msg in AllMessage.Split('\r'))
                                        {
                                            sw.WriteLine(msg);
                                        }
                                    }
                                }

                                    // создаем .ok файл в папке для рез-тов
                                    if (OkFileName != "")
                                    {
                                        if (!File.Exists(CGMPath + @"\" + OkFileName))
                                    {
                                        using (StreamWriter sw = File.CreateText(CGMPath + @"\" + OkFileName))
                                        {
                                            sw.WriteLine("ok");
                                        }
                                    }
                                        else
                                    {
                                        File.Delete(CGMPath + OkFileName);
                                        using (StreamWriter sw = File.CreateText(CGMPath + @"\" + OkFileName))
                                        {
                                            sw.WriteLine("ok");
                                        }
                                    }
                                    }

                                    // помещение файла в архивную папку
                                    if (File.Exists(ArchivePath + @"\" + FileName))
                                    {
                                        File.Delete(ArchivePath + @"\" + FileName);
                                    }
                                    File.Move(file, ArchivePath + @"\" + FileName);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    // помещение файла в папку с ошибками
                                    if (File.Exists(ErrorPath + @"\" + FileName))
                                    {
                                        File.Delete(ErrorPath + @"\" + FileName);
                                    }
                                    File.Move(file, ErrorPath + @"\" + FileName);
                                }
                          }
                          // или сюда блок else  и помещение файла в ошибки
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

        }
        #endregion
        static void Main(string[] args)
        {
            ServiceIsActive = true;
            Console.WriteLine("Start working!");
            ResultsProcessing();

            /*
            // Поток обработки результатов
            Thread ResultProcessingThread = new Thread(ResultsProcessing);
            ResultProcessingThread.Name = "ResultsProcessing";
            //ListOfThreads.Add(ResultProcessingThread);
            ResultProcessingThread.Start();
            */
            Console.WriteLine("Hello World!");
            Console.ReadLine();
        }
    }
}
