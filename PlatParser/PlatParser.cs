using System;
using System.IO;
using System.Runtime.Remoting.Messaging;
using NLog;

namespace PlatParser
{
    public class PlatParser
    {
        static readonly NLog.Logger _log = LoggerFactory.GetLogger(typeof(PlatParser).FullName);
        
        public static void Main(string[] args)
        {
            ConfigureLogging();
            var itemDb = PlatValueDatabase.Parse(new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ItemValues.txt"))); 
            var session = PlatSession.Parse(new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt")), itemDb);
            session.PrintReport();
        }

        
        private static void ConsoleLog(NLog.Logger logger, LogLevel logLevel, string message)
        {
            System.Console.WriteLine("[" + logLevel + "] " + message);
        }
        
        private static void ConfigureLogging()
        {
            LoggerFactory.globalLogLevel = LogLevel.On;
            LoggerFactory.AddAppender(ConsoleLog); 
        }
    }
}