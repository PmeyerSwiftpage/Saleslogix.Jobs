using Sage.Entity.Interfaces;
using Sage.Platform.Application;
using Sage.Platform.Orm.Attributes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Xml.Serialization;

namespace SalesLogix.FiscalAPI
{
    class Platform
    {
        public static int GetStartofFiscalYear()
        {
            int month = 1;		// Default is January
            IList<ICustomSetting> fiscalSettings = GetFiscalSettings();

            foreach (ICustomSetting setting in fiscalSettings)
            {
                if (setting.Description == "FiscalYearStart")
                {
                    month = Convert.ToInt16(setting.DataValue);
                    break;
                }
            }
            return month;
        }

        public static int GetQAndFPeriod()
        {
            int period = 1;		// Default is by Month
            IList<ICustomSetting> fiscalSettings = GetFiscalSettings();

            foreach (ICustomSetting setting in fiscalSettings)
            {
                if (setting.Description == "QutoaAndForecastBy")
                {
                    period = Convert.ToInt16(setting.DataValue);
                    break;
                }
            }
            return period;
        }

        public static DateTime? FirstDayofPeriod(DateTime? date)
        {
            date = Date.FirstDayofMonth(date);
            int period = GetQAndFPeriod();

            if (period != 1)
            {
                IList<FiscalYearPeriod> fiscalYear = GetFiscalYear();
                int month = date.Value.Month;
                IFormatProvider culture = new System.Globalization.CultureInfo("en-US", true);

                foreach (FiscalYearPeriod fyp in fiscalYear)
                {
                    if (month >= fyp.startMonth && month <= fyp.endMonth)
                    {
                        date = Convert.ToDateTime(String.Format("{0}/1/{1}", fyp.startMonth, date.Value.Year), culture).Date;
                        break;
                    }
                }
            }
            return date;
        }

        public static DateTime? LastDayofPeriod(DateTime? date)
        {
            date = Date.LastDayofMonth(date);
            int period = GetQAndFPeriod();

            if (period != 1)
            {
                IList<FiscalYearPeriod> fiscalYear = GetFiscalYear();
                int month = date.Value.Month;
                IFormatProvider culture = new System.Globalization.CultureInfo("en-US", true);

                foreach (FiscalYearPeriod fyp in fiscalYear)
                {
                    if (month >= fyp.startMonth && month <= fyp.endMonth)
                    {
                        date = Convert.ToDateTime(String.Format("{0}/1/{1}", fyp.endMonth, date.Value.Year), culture).Date.AddHours(23).AddMinutes(59).AddSeconds(59);
                        date = Date.LastDayofMonth(date);
                        break;
                    }
                }
            }
            return date;
        }

        public static IList<ICustomSetting> GetFiscalSettings()
        {
            IList<ICustomSetting> customSettings = new List<ICustomSetting>();

            using (var session = new Sage.Platform.Orm.SessionScopeWrapper())
            {
                try
                {
                    customSettings = session.QueryOver<ICustomSetting>()
                        .Where(cs1 => cs1.Category == "Fiscal")
                        .List<ICustomSetting>();
                }
                catch { }

                return customSettings;
            }
        }

        public static IList<FiscalYearPeriod> GetFiscalYear()
        {
            int month = GetStartofFiscalYear();
            int period = GetQAndFPeriod();
            IList<FiscalYearPeriod> fiscalYear = new List<FiscalYearPeriod>();

            for (int i = 1; i <= (12 / period); i++)
            {
                FiscalYearPeriod fyp = new FiscalYearPeriod();
                fyp.period = i;
                fyp.startMonth = month;
                fyp.endMonth = month + (period - 1);
                if (fyp.endMonth > 12)
                {
                    fyp.endMonth = fyp.endMonth - 12;
                }
                fiscalYear.Add(fyp);
            }
            return fiscalYear;
        }


        #region Private Methods
        private static string GetIdFromQueryString(string queryString)
        {
            string id = null;

            string[] temp1 = queryString.Split('=');
            string[] temp2 = temp1[1].Split('&');

            id = temp2[0];

            return id;
        }
        #endregion
    }

    class FiscalYearPeriod
    {
        public int period { get; set; }
        public int startMonth { get; set; }
        public int endMonth { get; set; }
    }

    class Date
    {
        public static DateTime? FirstDayofMonth(DateTime? _date)
        {
            return _date.Value.AddDays(1 - _date.Value.Day).Date;
        }

        public static DateTime? FirstDayofMonth()
        {
            return FirstDayofMonth(DateTime.UtcNow);
        }

        public static DateTime? LastDayofMonth(DateTime? _date)
        {
            return _date.Value.AddMonths(1).AddDays(-1).Date.AddHours(23).AddMinutes(59).AddSeconds(59);
        }

        public static DateTime? LastDayofMonth()
        {
            return LastDayofMonth(DateTime.UtcNow);
        }

        public static string ToMonthName(DateTime? _date)
        {
            return CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(_date.Value.Month);
        }

        public static string ToShortMonthName(DateTime? _date)
        {
            return CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(_date.Value.Month);
        }

        public static string MonthName(int _month = 1)
        {
            if (_month < 1 || _month > 12)
            {
                _month = 1;
            }
            return CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(_month);
        }

        public static DateTime EndOfDay(DateTime d)
        {
            return (d.Date.AddHours(23).AddMinutes(59).AddSeconds(59));
        }
    }
}
