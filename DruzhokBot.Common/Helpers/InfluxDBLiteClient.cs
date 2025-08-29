using System.Text;
using DruzhokBot.Domain;
using InfluxData.Net.InfluxDb;
using InfluxData.Net.InfluxDb.Models;

namespace DruzhokBot.Common.Helpers;

public static class InfluxDbLiteClient
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly InfluxDbClient _client = 
        new ("http://localhost:8086", "", "", InfluxData.Net.Common.Enums.InfluxDbVersion.v_1_3);
    
    
    public static void Query(string tableName, Dictionary<string, object> tags, Dictionary<string, object> fields)
    {
        Task.Run(async () =>
        {
            try
            {
                var point = new Point
                {
                    Name = tableName,
                    Tags = tags,
                    Fields = fields,
                    Timestamp = DateTime.UtcNow
                };

                await _client.Client.WriteAsync(point, Consts.LogsDbName);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                Logger.Error($"query: {tags}, {fields}");
            }
        });
    }
}
