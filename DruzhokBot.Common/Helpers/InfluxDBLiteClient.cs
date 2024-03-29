﻿using System.Text;

namespace DruzhokBot.Common.Helpers;

public static class InfluxDbLiteClient
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    public static void Query(string query)
    {
        var influxDbQuery = Environment.GetEnvironmentVariable("DRUZHOKBOT_INFLUX_QUERY");

        if (influxDbQuery != null)
        {
            new Task(() =>
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.BaseAddress = new Uri(influxDbQuery);
                        client.Timeout = TimeSpan.FromSeconds(1);
                        var content = new System.Net.Http.StringContent(query, Encoding.UTF8, "application/Text");
                        var res = client.PostAsync("", content).Result;
                        var tt = res.Content.ReadAsStringAsync().Result;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                    Logger.Error($"query: {query}");
                }
            }).Start();
        }
    }
}
