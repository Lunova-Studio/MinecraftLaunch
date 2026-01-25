using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using System.Text.RegularExpressions;

namespace MinecraftLaunch.Components.Parser;

public static partial class MinecraftLoggingParser
{
    public static MinecraftLogEntry Parse(string log) => new()
    {
        SourceText = log,
        Log = GetLog(log),
        Source = GetSource(log),
        Time = TimeRegex().IsMatch(log) ? GetLogTime(log) : DateTime.Now.ToString("T"),
        LogLevel = GetLogType(log) switch
        {
            "FATAL" => MinecraftLogLevel.Fatal,
            "ERROR" => MinecraftLogLevel.Error,
            "WARN" => MinecraftLogLevel.Warning,
            "INFO" => MinecraftLogLevel.Info,
            "DEBUG" => MinecraftLogLevel.Debug,
            "STACK" => MinecraftLogLevel.StackTrace,
            "Exception" => MinecraftLogLevel.Exception,
            _ => MinecraftLogLevel.Unknown
        },
    };

    public static string GetLog(string log)
    {
        var res = GetTotalPrefix(log);
        var s = log.Split(res);
        return (s.Length >= 2 ? s[1] : log).Trim();
    }

    /// <summary>
    /// 获取日志等级类型
    /// </summary>
    /// <param name="log"></param>
    /// <returns></returns>
    public static string GetLogType(string log)
    {
        //是否是堆栈信息
        if (StackTraceRegex().IsMatch(log))
        {
            return "STACK";
        }

        //是否是异常信息
        if (ExceptionRegex().IsMatch(log))
        {
            return "Exception";
        }

        return LogTypeRegex().Match(log).Value;
    }

    /// <summary>
    /// 获取日志源
    /// </summary>
    /// <param name="log"></param>
    /// <returns></returns>
    public static string GetSource(string log)
    {
        var content = SourceRegex()
            .Match(log)
            .Value
            .Split('/')
            .FirstOrDefault();

        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var time = TimeFullRegex().Match(log).Value;
        var result = content.Replace($"{time} [", string.Empty);

        return result;
    }

    /// <summary>
    /// 获取日志时间
    /// </summary>
    /// <param name="log"></param>
    /// <returns></returns>
    public static string GetLogTime(string log)
    {
        return TimeRegex().Match(log).Value;
    }

    /// <summary>
    /// 获取日志所有前缀
    /// </summary>
    /// <param name="log"></param>
    /// <returns></returns>
    public static string GetTotalPrefix(string log)
    {
        return TotalPrefixRegex().Match(log).Value;
    }

    #region Privates

    [GeneratedRegex("(20|21|22|23|[0-1]\\d):[0-5]\\d:[0-5]\\d")]
    private static partial Regex TimeRegex();

    [GeneratedRegex("\\[(20|21|22|23|[0-1]\\d):[0-5]\\d:[0-5]\\d\\]")]
    private static partial Regex TimeFullRegex();

    [GeneratedRegex("(at .*)")]
    private static partial Regex StackTraceRegex();

    [GeneratedRegex("(?m)^.*?Exception.*")]
    private static partial Regex ExceptionRegex();

    [GeneratedRegex("FATAL|ERROR|WARN|INFO|DEBUG")]
    private static partial Regex LogTypeRegex();

    [GeneratedRegex("[\\w\\W\\s]{2,}/(FATAL|ERROR|WARN|INFO|DEBUG)")]
    private static partial Regex SourceRegex();

    [GeneratedRegex("\\[(20|21|22|23|[0-1]\\d):[0-5]\\d:[0-5]\\d\\] \\[[\\w\\W\\s]{2,}/(FATAL|ERROR|WARN|INFO|DEBUG)\\]")]
    private static partial Regex TotalPrefixRegex();

    #endregion Privates
}