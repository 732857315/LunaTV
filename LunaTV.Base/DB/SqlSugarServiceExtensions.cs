using LunaTV.Base.DB.UnitOfWork;
using LunaTV.Base.Models;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace LunaTV.Base.DB;

public static class SqlSugarServiceExtensions
{
    public static IServiceCollection AddSqlSugarClient(this IServiceCollection services, string dbPath)
    {
        var db = new SqlSugarClient(new ConnectionConfig()
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={dbPath};",
            IsAutoCloseConnection = true, // 自动释放连接 
            InitKeyType = InitKeyType.Attribute, // 主键配置方式
            MoreSettings = new ConnMoreSettings()
            {
                SqliteCodeFirstEnableDescription = true //启用备注
            }
        });
        db.Aop.OnLogExecuted = (sql, pars) => { Console.WriteLine(sql); };

        // 创建数据库表
        if (!File.Exists(dbPath) || db.DbMaintenance.GetTableInfoList(false).Count != GetDbTypes().Length)
        {
            try
            {
                db.CodeFirst.SetStringDefaultLength(200).InitTables(GetDbTypes());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        if (!db.DbMaintenance.IsAnyColumn("player_config", "DoubanApiEnabled"))
        {
            db.DbMaintenance.AddColumn("player_config", new DbColumnInfo()
            {
                DbColumnName = "DoubanApiEnabled",
                TableName = "player_config",
                DataType = "bit",
                IsNullable = false,
                DefaultValue = "0"
            });
            db.DbMaintenance.AddColumn("player_config", new DbColumnInfo()
            {
                DbColumnName = "HomeAutoLoadDoubanEnabled",
                TableName = "player_config",
                DataType = "bit",
                IsNullable = false,
                DefaultValue = "0"
            });
            db.DbMaintenance.AddColumn("player_config", new DbColumnInfo()
            {
                DbColumnName = "ForceApiNeedSpecialSource",
                TableName = "player_config",
                DataType = "bit",
                IsNullable = false,
                DefaultValue = "0"
            });
        }

        if (!db.DbMaintenance.IsAnyColumn("player_config", "DoubanMovieTags"))
        {
            db.DbMaintenance.AddColumn("player_config", new DbColumnInfo()
            {
                DbColumnName = "DoubanMovieTags",
                TableName = "player_config",
                DataType = "varchar(2000)",
                IsNullable = true
            });
        }

        if (!db.DbMaintenance.IsAnyColumn("player_config", "DoubanTvTags"))
        {
            db.DbMaintenance.AddColumn("player_config", new DbColumnInfo()
            {
                DbColumnName = "DoubanTvTags",
                TableName = "player_config",
                DataType = "varchar(2000)",
                IsNullable = true
            });
        }

        if (!db.DbMaintenance.IsAnyColumn("media_download", "DownloadStatus"))
            AddMediaDownloadColumn("DownloadStatus", "int", false, "0");
        if (!db.DbMaintenance.IsAnyColumn("media_download", "Progress"))
            AddMediaDownloadColumn("Progress", "real", false, "0");
        if (!db.DbMaintenance.IsAnyColumn("media_download", "DownloadedBytes"))
            AddMediaDownloadColumn("DownloadedBytes", "bigint", false, "0");
        if (!db.DbMaintenance.IsAnyColumn("media_download", "TotalBytes"))
            AddMediaDownloadColumn("TotalBytes", "bigint", false, "0");
        if (!db.DbMaintenance.IsAnyColumn("media_download", "SizeText"))
            AddMediaDownloadColumn("SizeText", "varchar(2000)", true);
        if (!db.DbMaintenance.IsAnyColumn("media_download", "SpeedText"))
            AddMediaDownloadColumn("SpeedText", "varchar(2000)", true);
        if (!db.DbMaintenance.IsAnyColumn("media_download", "RemainingTimeText"))
            AddMediaDownloadColumn("RemainingTimeText", "varchar(2000)", true);
        if (!db.DbMaintenance.IsAnyColumn("media_download", "ErrorMessage"))
            AddMediaDownloadColumn("ErrorMessage", "varchar(2000)", true);
        if (!db.DbMaintenance.IsAnyColumn("media_download", "OutputFilePath"))
            AddMediaDownloadColumn("OutputFilePath", "varchar(2000)", true);
        if (!db.DbMaintenance.IsAnyColumn("media_download", "Cover"))
            AddMediaDownloadColumn("Cover", "varchar(2000)", true);
        if (!db.DbMaintenance.IsAnyColumn("view_history", "Cover"))
        {
            db.DbMaintenance.AddColumn("view_history", new DbColumnInfo
            {
                DbColumnName = "Cover",
                TableName = "view_history",
                DataType = "varchar(2000)",
                IsNullable = true
            });
        }

        services.AddSingleton<ISqlSugarClient>(db);
        return services;

        void AddMediaDownloadColumn(string name, string dataType, bool isNullable, string? defaultValue = null)
        {
            db.DbMaintenance.AddColumn("media_download", new DbColumnInfo
            {
                DbColumnName = name,
                TableName = "media_download",
                DataType = dataType,
                IsNullable = isNullable,
                DefaultValue = defaultValue
            });
        }
    }

    public static void AddSugarRepository(this IServiceCollection services)
    {
        services.AddScoped<SugarRepository<SearchHistory>>();
        services.AddScoped<SugarRepository<ApiSource>>();
        services.AddScoped<SugarRepository<ViewHistory>>();
        services.AddScoped<SugarRepository<PlayerConfig>>();
        services.AddScoped<SugarRepository<MediaDownload>>();
    }

    public static Type[] GetDbTypes()
    {
        return
        [
            typeof(SearchHistory), typeof(ApiSource), typeof(ViewHistory), typeof(PlayerConfig), typeof(MediaDownload)
        ];
    }
}