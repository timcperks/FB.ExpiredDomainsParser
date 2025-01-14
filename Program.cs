﻿using AngleSharp;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace FB.ExpiredDomainsParser
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string proxystr = null, token = null;
            if (args.Length == 2)
            {
                proxystr = args[0];
                token = args[1];
            }

            if (string.IsNullOrEmpty(proxystr))
            {
                Console.WriteLine("Формат ввода - ip:port:login:password");
                Console.WriteLine("Прокси должен быть HTTP (не SOCKS)");
                Console.Write("Введите прокси (Enter, если нужен):");
                proxystr = Console.ReadLine();
            }
            if (string.IsNullOrEmpty(token))
            {
                Console.Write("Введите токен фб:");
                token = Console.ReadLine();
            }

            WebProxy proxy;
            HttpClient httpClient = new HttpClient();
            var client = new RestClient("https://graph.facebook.com/v7.0") { Timeout = -1 };
            var r = new Random();
            if (!string.IsNullOrEmpty(proxystr))
            {
                var split = proxystr.Split(':');
                proxy = new WebProxy(split[0], int.Parse(split[1]))
                {
                    Credentials = new NetworkCredential()
                    {
                        UserName = split[2],
                        Password = split[3]
                    }
                };
                var hch = new HttpClientHandler() { UseProxy = true, Proxy = proxy };
                httpClient = new HttpClient(hch);

                var tmp = r.Next(20, 99);
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.{tmp} (KHTML like Gecko) Chrome/78.0.3904.108 Safari/537.{tmp}");
                client.Proxy = proxy;
            }

            List<string> domains = new List<string>();
            var zones = new int[] { 2, 3, 4, 5, 7, 12, 19, 59, 69, 76, 1, 87, 94, 89, 119, 129, 154, 167, 247, 249, 674, 1129, 1065, 595, 660 };
            foreach (var z in zones)
            {
                Console.WriteLine($"Получаем списки доменов в зоне {z}...");
                var res = await httpClient.GetAsync($"https://www.expireddomains.net/deleted-domains?ftlds[]={z}");
                int i = 25;
                int dCount = 1;
                do
                {
                    try
                    {
                        var resContent = await res.Content.ReadAsStringAsync();
                        Console.WriteLine($"Получили список доменов #{dCount}.");
                        var config = Configuration.Default;
                        var context = BrowsingContext.New(config);
                        var doc = await context.OpenAsync(req => req.Content(resContent));
                        var newDomains = doc.All
                            .Where(el => el.ClassList.Contains("namelinks")).Select(el => el.InnerHtml).ToList();
                        Console.WriteLine($"Нашли {newDomains.Count} доменов.");
                        domains.AddRange(newDomains);
                        if (newDomains.Count == 0)
                        {
                            Console.WriteLine("Скорее всего список кончился, дальше не идём.");
                            break;
                        }
                        i += 25;
                        dCount++;
                        Console.WriteLine("Ждём...");
                        await Task.Delay(r.Next(1000, 3000));
                        res = await httpClient.GetAsync($"https://www.expireddomains.net/deleted-domains/?start={i}&ftlds[]={z}#listing");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Произошла ошибка: {e}");
                    }
                } while (i <= 300);
            }

            var found = new List<string>();
            var prefixes = new[] { "http://", "https://" };
            foreach (var d in domains)
            {
                if (d.Contains("...")) continue;
                foreach (var p in prefixes)
                {
                    var domain = $"{p}{d}";
                    Console.WriteLine($"Получаем лайки домена {domain}");
                    var request = new RestRequest(Method.GET);
                    request.AddQueryParameter("id", domain);
                    request.AddQueryParameter("scrape", "true");
                    request.AddQueryParameter("fields", "engagement");
                    request.AddQueryParameter("access_token", token);

                    IRestResponse response = client.Execute(request);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Console.WriteLine($"Не смогли получить лайки домена {domain}");
                        continue;
                    }
                    var obj = JObject.Parse(response.Content);
                    var count = int.Parse(obj["engagement"]["reaction_count"].ToString());
                    count += int.Parse(obj["engagement"]["comment_count"].ToString());
                    count += int.Parse(obj["engagement"]["share_count"].ToString());

                    if (count >= 1000)
                    {
                        Console.WriteLine($"Проверяем подробнее домен {domain}...");
                        request = new RestRequest(Method.GET);
                        request.AddQueryParameter("id", domain);
                        request.AddQueryParameter("scrape", "true");
                        request.AddQueryParameter("fields", "engagement");
                        request.AddQueryParameter("access_token", token);
                        response = client.Execute(request);
                        obj = JObject.Parse(response.Content);
                        count = int.Parse(obj["engagement"]["reaction_count"].ToString());
                        count += int.Parse(obj["engagement"]["comment_count"].ToString());
                        count += int.Parse(obj["engagement"]["share_count"].ToString());
                        if (count >= 1000)
                        {
                            found.Add($"{domain} : {count}");
                            Console.WriteLine($"{domain} : {count}");
                        }
                        else
                            Console.WriteLine("Нее, фуфло какое-то(");
                    }
                }
            }

            if (found.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Найдены годные домены!");
                found.ForEach(Console.WriteLine);
            }
            else Console.WriteLine("Не найдено ничего годного!((");
            Console.WriteLine("Нажмите любую клавишу для выхода.");
            Console.ReadKey();
        }
    }
}
