using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using NLog;

namespace PlatParser
{
    public class PlatValueDatabase
    {
        
        static readonly NLog.Logger _log = LoggerFactory.GetLogger(typeof(PlatValueDatabase).FullName);
        
        private Dictionary<string, decimal> _platValues;

        private void Init(FileInfo file)
        {
            _platValues = new Dictionary<string, decimal>();
            var lines = File.ReadAllLines(file.FullName);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    var parts = trimmed.Split(',');
                    var itemName = parts[0].Trim();
                    var valueRaw = parts[1].Trim();
                    decimal parsed;
                    if (Decimal.TryParse(valueRaw, out parsed))
                    {
                        _platValues[itemName] = parsed;
                    }
                    else
                    {
                        _log.Error("Failed to parse plat item " + itemName + " - " + valueRaw);
                    }
                }
                
                
            }
        }

        public bool GetPlatValue(string item, out decimal val)
        {
            if (_platValues.TryGetValue(item, out val))
            {
                return true;
            }

            return false;
        }

        public static PlatValueDatabase Parse(FileInfo file)
        {
            var res = new PlatValueDatabase();
            res.Init(file);
            return res;
        }
    }
}