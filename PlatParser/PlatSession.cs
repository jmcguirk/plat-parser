using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using NLog;

namespace PlatParser
{
    public class PlatSession
    {
        static readonly NLog.Logger _log = LoggerFactory.GetLogger(typeof(PlatSession).FullName);

        private const string TimestampFormat = "ddd MMM dd HH:mm:ss yyyy";
        private static decimal AverageFSValue = new decimal(4.0f);
        private const string FactionString = "Your faction standing with Vox";
        
        private const string PlatinumCurrency = "platinum";
        private const string GoldCurrency = "gold";
        private const string SilverCurrency = "silver";
        private const string CopperCurrency = "copper";

        private bool DestroySilver = true;
        private bool DestroyCopper = true;
        private bool DestroyFineSteel = true;

        private Dictionary<string, int> LootCounts = new Dictionary<string, int>();
        private Dictionary<string, int> DestroyedLootCounts = new Dictionary<string, int>();
        
        public Decimal PlatPerHour;
        public Decimal AccumulatedTotalPlat;
        public Decimal WastedFSPercentage;
        public Decimal WastedRawPercentage;
        public Decimal AccumulatedRawPlat;
        public Decimal AccumulatedRawGold;
        public Decimal AccumulatedItemPlat;
        public Decimal DestroyedRawPlat;
        public Decimal DestroyedFinesteelPlat;
        public decimal MinutesPerKill;
        public decimal PlatPerKill;
        public decimal GoldMix;
        
        private int KillsObserved;
        private int ItemsLooted;
        private int ItemsDestroyed;
        private int FSItems;

        private PlatValueDatabase _itemValueDatabase;

        private TimeSpan SessionDuration;
        private DateTime? SessionStartTime;
        private DateTime SessionEndTime;
        
        private void Init(FileInfo file, PlatValueDatabase valueDatabase)
        {
            _itemValueDatabase = valueDatabase;
            const int BufferSize = 128;
            using (var fileStream = File.OpenRead(file.FullName))
            using (var streamReader = new StreamReader(fileStream, System.Text.Encoding.UTF8, true, BufferSize)) {
                string line;
                while ((line = streamReader.ReadLine()) != null)
                {
                    var parts = line.Split(']');
                    if (parts.Length < 2)
                    {
                        continue;
                    }
                    var timestamp = parts[0].Trim();
                    DateTime ts = default(DateTime);
                    if (!string.IsNullOrEmpty(timestamp))
                    {
                        timestamp = parts[0].Substring(1);
                        try
                        {
                            ts = DateTime.ParseExact(timestamp, TimestampFormat, CultureInfo.InvariantCulture);
                        }
                        catch (Exception err)
                        {
                            _log.Error("Caught parse exception parsing " + timestamp);
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    var payload = parts[1].Trim();
                    if (payload.StartsWith("You receive") && payload.EndsWith("from the corpse."))
                    {
                        this.ParseRawPlatDrop(payload, ts);
                    }  else if (payload.StartsWith(FactionString))
                    {
                        this.ParseKill(payload, ts);
                    } else if (payload.StartsWith("sessionstart is not online at this time"))
                    {
                        this.ParseSessionStart(payload, ts);
                    } else if (payload.StartsWith("--You have looted a "))
                    {
                        this.ParseItemDrop(payload, ts);
                    }

                }
            }

            this.DoAnalysis();
            
        }

        private void DoAnalysis()
        {
            this.SessionDuration = this.SessionEndTime - this.SessionStartTime.Value;
            this.AccumulatedTotalPlat = this.AccumulatedRawPlat + this.AccumulatedItemPlat;
            this.PlatPerHour = this.AccumulatedTotalPlat / new decimal(this.SessionDuration.TotalHours);

            var hypotheticalFs = this.AccumulatedItemPlat + this.DestroyedFinesteelPlat;
            this.WastedFSPercentage = this.DestroyedFinesteelPlat / hypotheticalFs;
            this.WastedFSPercentage *= 100;
            var hypotheticalCoin = this.AccumulatedItemPlat + this.DestroyedRawPlat;
            this.WastedRawPercentage = this.DestroyedRawPlat / hypotheticalCoin;
            this.WastedRawPercentage *= 100;

            this.MinutesPerKill = new decimal(this.SessionDuration.TotalMinutes) / this.KillsObserved;
            this.PlatPerKill = this.AccumulatedTotalPlat / this.KillsObserved;

            this.GoldMix = this.AccumulatedRawGold / this.AccumulatedRawPlat;
            this.GoldMix *= 100;
        }

        private void ParseItemDrop(string payload, DateTime ts)
        {
            var itemName = payload.Substring(20); // You have looted a
            itemName = itemName.Substring(0, itemName.Length - 3);
            decimal itemValue;
            if (_itemValueDatabase.GetPlatValue(itemName, out itemValue))
            {
                this.AccumulatedItemPlat += itemValue;
                this.ItemsLooted++;
                int count;
                this.LootCounts.TryGetValue(itemName, out count);
                count++;
                this.LootCounts[itemName] = count;
            }
            else
            {
                int count;
                this.DestroyedLootCounts.TryGetValue(itemName, out count);
                count++;
                this.DestroyedLootCounts[itemName] = count;
                this.ItemsDestroyed++;
            }

            if (itemName.StartsWith("Fine Steel"))
            {
                this.FSItems++;
                if (this.DestroyFineSteel)
                {
                    this.DestroyedFinesteelPlat += AverageFSValue;
                }
            }
        }

        private void ResetSession(DateTime ts)
        {
            this.AccumulatedRawPlat = 0;
            this.AccumulatedItemPlat = 0;
            this.DestroyedRawPlat = 0;
            this.DestroyedFinesteelPlat = 0;
            this.KillsObserved = 0;
            this.SessionStartTime = ts;
            this.AccumulatedRawGold = 0;
            
            ItemsLooted = 0;
            ItemsDestroyed = 0;
            FSItems = 0;

            this.LootCounts.Clear();
            this.DestroyedLootCounts.Clear();
        }
        
        private void ParseSessionStart(string payload, DateTime ts)
        {
            this.ResetSession(ts);
        }

        private void ParseKill(string payload, DateTime ts)
        {
            if (!SessionStartTime.HasValue)
            {
                SessionStartTime = ts;
            }
            SessionEndTime = ts;
            this.KillsObserved++;
        }

        private void ParseRawPlatDrop(string payload, DateTime ts)
        {
            payload = payload.Substring(11).Trim(); // Trim "you receive"
            payload = payload.Substring(0, payload.Length - 16).Trim();
            string[] delimStrings = { "and" };
            var segments = payload.Split(delimStrings, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 1)
            {
                ParseRawPlatSegment(ts, segments[0]);
                ParseRawPlatSegment(ts, segments[1]);
            }
            else
            {
                ParseRawPlatSegment(ts, segments[0]);
            }
        }

        private void ParseRawPlatSegment(DateTime ts, string segment)
        {
            var parts = segment.Split(',');
            foreach (var drop in parts)
            {
                ParseRawPlatDropValue(ts, drop.Trim());
            }
        }

        private void ParseRawPlatDropValue(DateTime ts, string drop)
        {
            var parts = drop.Split(' ');
            var valRaw = parts[0];
            int value;
            decimal valueShifted = 0;
            if (!Int32.TryParse(valRaw, out value))
            {
                _log.Error("Failed to parse raw plat value " + valRaw) ;
            }
            var currency = parts[1];
            bool isSilver = false;
            bool isCopper = false;
            bool isGold = false;
            switch (currency)
            {
                case PlatinumCurrency:
                    valueShifted = value;
                    break;
                case GoldCurrency:
                    valueShifted = (decimal)value/10;
                    isGold = true;
                    break;
                case SilverCurrency:
                    valueShifted = (decimal)value/(100);
                    isSilver = true;
                    break;
                case CopperCurrency:
                    valueShifted = (decimal)value/(1000);
                    isCopper = true;
                    break;
            }

            if (isCopper && DestroyCopper)
            {
                this.DestroyedRawPlat += valueShifted;
                return;
            } 
            if (isSilver && DestroySilver)
            {
                this.DestroyedRawPlat += valueShifted;
                return;
            }

            if (isGold)
            {
                AccumulatedRawGold += valueShifted;
            }
            
            this.AccumulatedRawPlat += valueShifted;
        }

        public void PrintReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Session report - Total Duration: " + this.SessionDuration.TotalHours.ToString("N2") + " hours");
            sb.AppendLine(" ");
            sb.AppendLine("--------------Summary--------------------");
            sb.AppendLine("Total Plat: " + this.AccumulatedTotalPlat + "pp (" + this.PlatPerHour.ToString("N2") + "pp per hour)");
            sb.AppendLine("Total Raw Plat: " + this.AccumulatedRawPlat + "pp (" + this.GoldMix.ToString("N2") +"% gold)");
            sb.AppendLine("Total Item Plat: " + this.AccumulatedItemPlat + "pp (" + this.ItemsLooted + " items looted)");
            sb.AppendLine(" ");
            
            
            sb.AppendLine("--------------Waste----------------------");
            sb.AppendLine("Total Destroyed Raw Plat: " + this.DestroyedRawPlat + "pp (" + this.WastedRawPercentage.ToString("N2") + "% Waste)" ); 
            sb.AppendLine("Total Destroyed Fine Steel: " + this.DestroyedFinesteelPlat + "pp (" + this.WastedFSPercentage.ToString("N2") + "% Waste)" );
            sb.AppendLine(" ");
            sb.AppendLine("--------------Etc------------------------");
            sb.AppendLine("Kills Observed: " + this.KillsObserved + " (" + this.MinutesPerKill.ToString("N2") + " minutes per kill)" );
            sb.AppendLine("Average Plat Per Kill: " + this.PlatPerKill.ToString("N2") + "pp");
            sb.AppendLine("Items Destroyed: " + this.ItemsDestroyed.ToString());
            
            sb.AppendLine(" ");
            sb.AppendLine("--------------Loot------------------------");
            foreach (var kvp in this.LootCounts)
            {
                decimal val;
                this._itemValueDatabase.GetPlatValue(kvp.Key, out val);
                sb.AppendLine(kvp.Key + ": " + kvp.Value + " @ " + val.ToString("N2") + "pp ea");
            }
            foreach (var kvp in this.DestroyedLootCounts)
            {
                sb.AppendLine(kvp.Key + ": " + kvp.Value + " (destroyed)");
            }
            
            Console.Write(sb.ToString());
        }

        public static PlatSession Parse(FileInfo logFile, PlatValueDatabase itemDatabase)
        {
            PlatSession session = new PlatSession();
            session.Init(logFile, itemDatabase);
            return session;
        }
    }

}