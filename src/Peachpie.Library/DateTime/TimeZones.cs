/*

 Copyright (c) 2005-2006 Tomas Matousek.  

 The use and distribution terms for this software are contained in the file named License.txt, 
 which can be found in the root of the Phalanger distribution. By using this software 
 in any fashion, you are agreeing to be bound by the terms of this license.
 
 You must not remove this notice from this software.

*/

using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using System.ComponentModel;
using System.Runtime.InteropServices;

using System.Diagnostics;
using Pchp.Core;
using System.Xml;
using Pchp.Core.Utilities;

namespace Pchp.Library.DateTime
{
    /// <summary>
	/// Provides timezone information for PHP functions.
	/// </summary>
    public static class PhpTimeZone
    {
        private const string EnvVariableName = "TZ";

        [DebuggerDisplay("{PhpName} - {Info}")]
        private struct TimeZoneInfoItem
        {
            /// <summary>
            /// Comparer of <see cref="TimeZoneInfoItem"/>, comparing its <see cref="TimeZoneInfoItem.PhpName"/>.
            /// </summary>
            public class Comparer : IComparer<TimeZoneInfoItem>
            {
                public int Compare(TimeZoneInfoItem x, TimeZoneInfoItem y)
                {
                    return StringComparer.OrdinalIgnoreCase.Compare(x.PhpName, y.PhpName);
                }
            }

            /// <summary>
            /// PHP time zone name.
            /// </summary>
            public readonly string PhpName;

            /// <summary>
            /// Actual <see cref="TimeZoneInfo"/> from .NET.
            /// </summary>
            public readonly TimeZoneInfo Info;

            /// <summary>
            /// An abbrevation, not supported.
            /// </summary>
            public readonly string Abbrevation;

            /// <summary>
            /// Not listed item used only as an alias for another time zone.
            /// </summary>
            public readonly bool IsAlias;

            internal TimeZoneInfoItem(string/*!*/phpName, TimeZoneInfo/*!*/info, string abbrevation, bool isAlias)
            {
                // TODO: alter the ID with php-like name
                //if (!phpName.Equals(info.Id, StringComparison.OrdinalIgnoreCase))
                //{
                //    info = TimeZoneInfo.CreateCustomTimeZone(phpName, info.BaseUtcOffset, info.DisplayName, info.StandardName, info.DaylightName, info.GetAdjustmentRules());
                //}

                //
                this.PhpName = phpName;
                this.Info = info;
                this.Abbrevation = abbrevation;
                this.IsAlias = isAlias;
            }
        }

        /// <summary>
        /// Initializes list of time zones.
        /// </summary>
        static PhpTimeZone()
        {
            // initialize tz database (from system time zone database)
            timezones = InitializeTimeZones();
        }

        #region timezones

        /// <summary>
        /// PHP time zone database.
        /// </summary>
        private readonly static TimeZoneInfoItem[]/*!!*/timezones;

        private static TimeZoneInfoItem[]/*!!*/InitializeTimeZones()
        {
            // read list of initial timezones
            var sortedTZ = new SortedSet<TimeZoneInfoItem>(InitialTimeZones(), new TimeZoneInfoItem.Comparer());

            // add additional time zones:
            sortedTZ.Add(new TimeZoneInfoItem("UTC", TimeZoneInfo.Utc, null, false));
            sortedTZ.Add(new TimeZoneInfoItem("Etc/UTC", TimeZoneInfo.Utc, null, true));
            sortedTZ.Add(new TimeZoneInfoItem("Etc/GMT-0", TimeZoneInfo.Utc, null, true));
            sortedTZ.Add(new TimeZoneInfoItem("GMT", TimeZoneInfo.Utc, null, true));
            sortedTZ.Add(new TimeZoneInfoItem("GMT0", TimeZoneInfo.Utc, null, true));
            sortedTZ.Add(new TimeZoneInfoItem("UCT", TimeZoneInfo.Utc, null, true));
            sortedTZ.Add(new TimeZoneInfoItem("Universal", TimeZoneInfo.Utc, null, true));
            sortedTZ.Add(new TimeZoneInfoItem("Zulu", TimeZoneInfo.Utc, null, true));
            //sortedTZ.Add(new TimeZoneInfoItem("MET", sortedTZ.First(t => t.PhpName == "Europe/Rome").Info, null, true));
            //sortedTZ.Add(new TimeZoneInfoItem("WET", sortedTZ.First(t => t.PhpName == "Europe/Berlin").Info, null, true));     
            //{ "PRC"              
            //{ "ROC"              
            //{ "ROK"   
            // W-SU = 
            //{ "Poland"           
            //{ "Portugal"         
            //{ "PRC"              
            //{ "ROC"              
            //{ "ROK"              
            //{ "Singapore"      = Asia/Singapore  
            //{ "Turkey"  

            //
            return sortedTZ.ToArray();
        }

        static bool IsAlias(string id)
        {
            // whether to not display such tz within timezone_identifiers_list()
            var isphpname = id.IndexOf('/') >= 0 || id.IndexOf("GMT", StringComparison.Ordinal) >= 0 && id.IndexOf(' ') < 0;
            return !isphpname;
        }

        static IEnumerable<TimeZoneInfoItem>/*!!*/InitialTimeZones()
        {
            // time zone cache:
            var tzcache = new Dictionary<string, TimeZoneInfo>(128, StringComparer.OrdinalIgnoreCase);
            Func<string, TimeZoneInfo> cachelookup = (id) =>
            {
                TimeZoneInfo tz;
                if (!tzcache.TryGetValue(id, out tz))
                {
                    TimeZoneInfo winTZ = null;
                    try
                    {
                        winTZ = TimeZoneInfo.FindSystemTimeZoneById(id);
                    }
                    catch { }

                    tzcache[id] = tz = winTZ;   // null in case "id" is not defined in Windows registry (probably missing Windows Update)
                }

                return tz;
            };

            // system timezones:
            var tzns = TimeZoneInfo.GetSystemTimeZones();
            if (tzns != null)
            {
                foreach (var x in tzns)
                {
                    tzcache[x.Id] = x;
                    yield return new TimeZoneInfoItem(x.Id, x, null, IsAlias(x.Id));
                }
            }

            // collect php time zone names and match them with Windows TZ IDs:
            using (var xml = XmlReader.Create(new System.IO.StringReader(Resources.Resources.WindowsTZ)))
            {
                while (xml.Read())
                {
                    switch (xml.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (xml.Name == "mapZone")
                            {
                                // <mapZone other="Dateline Standard Time" type="Etc/GMT+12"/>

                                var windowsTZ = cachelookup(xml.GetAttribute("other"));  // Windows ID
                                if (windowsTZ != null)
                                {
                                    var phpIds = xml.GetAttribute("type");
                                    if (phpIds != null)
                                    {
                                        // map php TZ name to Windows TZ if not defined by OS (Unix)
                                        foreach (var phpTzName in phpIds.Split(' '))
                                        {
                                            Debug.Assert(!string.IsNullOrWhiteSpace(phpTzName));
                                            if (!tzcache.ContainsKey(phpTzName))
                                            {
                                                tzcache[phpTzName] = windowsTZ; //  write back to cache
                                                yield return new TimeZoneInfoItem(phpTzName, windowsTZ, null, IsAlias(phpTzName));
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }
            }

            //
            //{ "US/Alaska"        
            //{ "US/Aleutian"      
            //{ "US/Arizona"       
            yield return new TimeZoneInfoItem("US/Central", cachelookup("Central Standard Time"), null, true);
            //{ "US/East-Indiana"  
            //{ "US/Eastern"       
            yield return new TimeZoneInfoItem("US/Hawaii", cachelookup("Hawaiian Standard Time"), null, true);
            //{ "US/Indiana-Starke"
            //{ "US/Michigan"      
            //{ "US/Mountain"      
            //{ "US/Pacific"       
            //{ "US/Pacific-New"   
            //{ "US/Samoa"   
        }

        #endregion

        /// <summary>
        /// Gets the current time zone for PHP date-time library functions. Associated with the current context.
        /// </summary>
        /// <remarks>It returns the time zone set by date_default_timezone_set PHP function.
        /// If no time zone was set, the time zone is determined in following order:
        /// 1. the time zone set in configuration
        /// 2. the time zone of the current system
        /// 3. default UTC time zone</remarks>
        internal static TimeZoneInfo GetCurrentTimeZone(Context ctx)
        {
            var info = ctx.TryGetProperty<TimeZoneInfo>();

            // if timezone is set by date_default_timezone_set(), return it

            if (info == null)
            {
                // default timezone was not set, use & cache the current timezone
                var cache = ctx.TryGetProperty<CurrentTimeZoneCache>();
                if (cache == null)
                {
                    ctx.SetProperty(cache = new CurrentTimeZoneCache());
                }

                info = cache.TimeZone;
            }

            //
            return info;
        }

        internal static void SetCurrentTimeZone(Context ctx, TimeZoneInfo value)
        {
            ctx.SetProperty(value);
        }

        #region CurrentTimeZoneCache

        /// <summary>
        /// Cache of current TimeZone with auto-update ability.
        /// </summary>
        private class CurrentTimeZoneCache
        {
            public CurrentTimeZoneCache()
            {
            }
#if DEBUG
            internal CurrentTimeZoneCache(TimeZoneInfo timezone)
            {
                this._timeZone = timezone;
                this._changedFunc = (_) => false;
            }
#endif

            /// <summary>
            /// Get the TimeZone set by the current process. Depends on environment variable, or local configuration, or system time zone.
            /// </summary>
            public TimeZoneInfo TimeZone
            {
                get
                {
                    if (_timeZone == null || _changedFunc == null || _changedFunc(_timeZone) == true)
                        _timeZone = DetermineTimeZone(out _changedFunc);    // get the current timezone, update the function that determines, if the timezone has to be rechecked.

                    return _timeZone;
                }
            }

            private TimeZoneInfo _timeZone;

            /// <summary>
            /// Function that determines if the current timezone should be rechecked.
            /// </summary>
            private Func<TimeZoneInfo/*!*/, bool> _changedFunc;

            /// <summary>
            /// Finds out the time zone in the way how PHP does.
            /// </summary>
            private static TimeZoneInfo DetermineTimeZone(out Func<TimeZoneInfo, bool> changedFunc)
            {
                TimeZoneInfo result;

                //// check environment variable:
                //string env_tz = System.Environment.GetEnvironmentVariable(EnvVariableName);
                //if (!string.IsNullOrEmpty(env_tz))
                //{
                //    result = GetTimeZone(env_tz);
                //    if (result != null)
                //    {
                //        // recheck the timezone only if the environment variable changes
                //        changedFunc = (timezone) => !String.Equals(timezone.StandardName, System.Environment.GetEnvironmentVariable(EnvVariableName), StringComparison.OrdinalIgnoreCase);
                //        // return the timezone set in environment
                //        return result;
                //    }

                //    PhpException.Throw(PhpError.Notice, LibResources.GetString("unknown_timezone_env", env_tz));
                //}

                //// check configuration:
                //LibraryConfiguration config = LibraryConfiguration.Local;
                //if (config.Date.TimeZone != null)
                //{
                //    // recheck the timezone only if the local configuration changes, ignore the environment variable from this point at all
                //    changedFunc = (timezone) => LibraryConfiguration.Local.Date.TimeZone != timezone;
                //    return config.Date.TimeZone;
                //}

                // convert current system time zone to PHP zone:
                result = SystemToPhpTimeZone(TimeZoneInfo.Local);

                // UTC:
                if (result == null)
                    result = DateTimeUtils.UtcTimeZone;// GetTimeZone("UTC");

                //PhpException.Throw(PhpError.Strict, LibResources.GetString("using_implicit_timezone", result.Id));

                // recheck the timezone when the TimeZone in local configuration is set
                changedFunc = (timezone) => false;//LibraryConfiguration.Local.Date.TimeZone != null;
                return result;
            }

        }

        #endregion

        ///// <summary>
        ///// Gets/sets/resets legacy configuration setting "date.timezone".
        ///// </summary>
        //internal static object GsrTimeZone(LibraryConfiguration/*!*/ local, LibraryConfiguration/*!*/ @default, object value, IniAction action)
        //{
        //    string result = (local.Date.TimeZone != null) ? local.Date.TimeZone.StandardName : null;

        //    switch (action)
        //    {
        //        case IniAction.Set:
        //            {
        //                string name = Core.Convert.ObjectToString(value);
        //                TimeZoneInfo zone = GetTimeZone(name);

        //                if (zone == null)
        //                {
        //                    PhpException.Throw(PhpError.Warning, LibResources.GetString("unknown_timezone", name));
        //                }
        //                else
        //                {
        //                    local.Date.TimeZone = zone;
        //                }
        //                break;
        //            }

        //        case IniAction.Restore:
        //            local.Date.TimeZone = @default.Date.TimeZone;
        //            break;
        //    }
        //    return result;
        //}

        /// <summary>
        /// Gets an instance of <see cref="TimeZone"/> corresponding to specified PHP name for time zone.
        /// </summary>
        /// <param name="phpName">PHP time zone name.</param>
        /// <returns>The time zone or a <B>null</B> reference.</returns>
        internal static TimeZoneInfo GetTimeZone(string/*!*/ phpName)
        {
            if (string.IsNullOrEmpty(phpName))
                return null;

            // simple binary search (not the Array.BinarySearch)
            var timezones = PhpTimeZone.timezones;
            int a = 0, b = timezones.Length - 1;
            while (a <= b)
            {
                int x = (a + b) >> 1;
                int comparison = StringComparer.OrdinalIgnoreCase.Compare(timezones[x].PhpName, phpName);
                if (comparison == 0)
                    return timezones[x].Info;

                if (comparison < 0)
                    a = x + 1;
                else //if (comparison > 0)
                    b = x - 1;
            }

            return null;
        }

        /// <summary>
        /// Tries to match given <paramref name="systemTimeZone"/> to our fixed <see cref="timezones"/>.
        /// </summary>
        static TimeZoneInfo SystemToPhpTimeZone(TimeZoneInfo systemTimeZone)
        {
            if (systemTimeZone == null)
                return null;

            var tzns = timezones;
            for (int i = 0; i < tzns.Length; i++)
            {
                var tz = tzns[i].Info;
                if (tz != null && tz.DisplayName.Equals(systemTimeZone.DisplayName, StringComparison.OrdinalIgnoreCase)) // TODO: && tz.HasSameRules(systemTimeZone))
                    return tz;
            }

            return null;
        }

        #region date_default_timezone_get, date_default_timezone_set

        public static bool date_default_timezone_set(Context ctx, string zoneName)
        {
            var zone = GetTimeZone(zoneName);
            if (zone == null)
            {
                PhpException.Throw(PhpError.Notice, Resources.LibResources.unknown_timezone, zoneName);
                return false;
            }

            SetCurrentTimeZone(ctx, zone);
            return true;
        }

        public static string date_default_timezone_get(Context ctx)
        {
            var timezone = GetCurrentTimeZone(ctx);
            return (timezone != null) ? timezone.Id : null;
        }

        #endregion

        #region timezone_identifiers_list, timezone_version_get

        static readonly Dictionary<string, int> s_what = new Dictionary<string, int>(10, StringComparer.OrdinalIgnoreCase)
        {
            {"africa", DateTimeZone.AFRICA},
            {"america", DateTimeZone.AMERICA},
            {"antarctica", DateTimeZone.ANTARCTICA},
            {"artic", DateTimeZone.ARCTIC},
            {"asia", DateTimeZone.ASIA},
            {"atlantic", DateTimeZone.ATLANTIC},
            {"australia", DateTimeZone.AUSTRALIA},
            {"europe", DateTimeZone.EUROPE},
            {"indian", DateTimeZone.INDIAN},
            {"pacific", DateTimeZone.PACIFIC},
            {"etc", DateTimeZone.UTC},
        };

        /// <summary>
        /// Gets zone constant.
        /// </summary>
        static int GuessWhat(TimeZoneInfoItem tz)
        {
            int slash = tz.PhpName.IndexOf('/');
            if (slash > 0)
            {
                s_what.TryGetValue(tz.PhpName.Remove(slash), out int code);
                return code;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Returns a numerically indexed array containing all defined timezone identifiers.
        /// </summary>
        public static PhpArray timezone_identifiers_list(int what = DateTimeZone.ALL, string country = null)
        {
            if ((what & DateTimeZone.PER_COUNTRY) == DateTimeZone.PER_COUNTRY || !string.IsNullOrEmpty(country))
            {
                throw new NotImplementedException();
            }

            var timezones = PhpTimeZone.timezones;

            // copy names to PHP array:
            var array = new PhpArray(timezones.Length);
            for (int i = 0; i < timezones.Length; i++)
            {
                if (timezones[i].IsAlias)
                {
                    continue;
                }

                if (what == DateTimeZone.ALL || what == DateTimeZone.ALL_WITH_BC || (what & GuessWhat(timezones[i])) != 0)
                {
                    array.Add(timezones[i].PhpName);
                }
            }

            //
            return array;
        }

        /// <summary>
        /// Gets the version of used the time zone database.
        /// </summary>
        public static string timezone_version_get()
        {
            //try
            //{
            //    using (var reg = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones"))
            //        return reg.GetValue("TzVersion", 0).ToString() + ".system";
            //}
            //catch { }
            Debug.WriteLine("TODO: timezone_version_get()");    // TODO: timezone_version_get

            // no windows update installed
            return "0.system";
        }

        #endregion

        #region timezone_open, timezone_offset_get

        /// <summary>
        /// Alias of new <see cref="DateTimeZone"/>
        /// </summary>
        [return: CastToFalse]
        public static DateTimeZone timezone_open(Context/*!*/context, string timezone)
        {
            var tz = GetTimeZone(timezone);
            if (tz == null)
                return null;

            return new DateTimeZone(context, tz);
        }

        /// <summary>
        /// Alias of <see cref="DateTimeZone.getOffset"/>
        /// </summary>
        [return: CastToFalse]
        public static int timezone_offset_get(Context context, DateTimeZone timezone, Library.DateTime.DateTime datetime)
        {
            return (timezone != null) ? timezone.getOffset(datetime) : -1;
        }

        [return: CastToFalse]
        public static PhpArray timezone_transitions_get(DateTimeZone timezone)
        {
            return (timezone != null) ? timezone.getTransitions() : null;
        }

        #endregion
    }
}
