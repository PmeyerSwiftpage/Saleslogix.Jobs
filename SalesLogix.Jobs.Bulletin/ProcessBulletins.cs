using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sage.Entity.Interfaces;
using Sage.Platform;
using Sage.Platform.Orm;
using Sage.Platform.Scheduling;
using Quartz;
using log4net;
using SalesLogix.Jobs.Bulletin.Localization;
using SalesLogix.Jobs.Bulletin.Properties;


namespace SalesLogix.Jobs.Bulletin
{
    
    [DisallowConcurrentExecution]
    [SRDisplayName(SR.Job_ProcessBulletins_DisplayName)]
    [SRDescription(SR.Job_ProcessBulletins_Description)]
    public class ProcessBulletins : SystemJobBase
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region Public Properties
        [SRDescription(SR.Job_ProcessBulletins_BulletinName_Description)]
        [SRDisplayName(SR.Job_ProcessBulletins_BulletinName_DisplayName)]
        public string BulletinName { get; set; }

        #endregion

        private bool _interrupted = false;
        protected override void OnInterrupt()
        {
            base.OnInterrupt();
            _interrupted = Interrupted;
        }

        protected override void OnExecute()
        {
            if (String.IsNullOrEmpty(BulletinName))
            {
                throw new InvalidOperationException(Resources.Job_BulletinName_NotSpecified);
            }

            using (var session = new SessionScopeWrapper())
            {
                IBulletin bulletin = session.QueryOver<IBulletin>()
                    .Where(x => x.BulletinName == BulletinName)
                    .SingleOrDefault<IBulletin>();

                if (bulletin == null)
                {
                    throw new InvalidOperationException(Resources.Job_BulletinName_NotFound);
                }

                switch (bulletin.BulletinName)
                {
                    case "Weekly Forecast Summary":
                        WeeklyForecastSummary(bulletin);
                        break;
                }
            }
        }

        private void WeeklyForecastSummary(IBulletin bulletin)
        {
            DateTime today = DateTime.UtcNow;
            ForecastSummary fs = new ForecastSummary();

            Progress = 0;
            decimal counter = 0;

            using (var session = new SessionScopeWrapper())
            {
                IList<IForecast> forecasts = session.QueryOver<IForecast>()
                    .Where(x => today >= x.BeginDate && today <= x.EndDate && x.Active == true)
                    .List<IForecast>();

                foreach (IForecast f in forecasts)
                {
                    ForecastInfo fi = new ForecastInfo();
                    fi.ForecastName = f.Description;
                    fi.AssignedTo = f.AssignedTo.UserInfo.NameLF;
                    fi.Amount = Convert.ToDecimal(f.Amount);
                    fi.Pipeline = (decimal)f.PeriodPipelineAmt("Avg");
                    fi.ClosedWon = (decimal)f.PeriodClosedWonAmt();
                    fi.Quota = GetQuotaAmtforUser(f.AssignedTo, today);

                    fs.forecasts.Add(fi);
                    Progress = (100M * ++counter / forecasts.Count)/2;
                }
            }

            counter = 0;
            if (fs.forecasts.Count > 0)
            {
                string msgBody = BuildMessageBody(fs);
                string subject = "Weekly Forecast Summary";

                IDeliveryItem di = Sage.Platform.EntityFactory.Create<IDeliveryItem>();
                di.Body = msgBody;
                di.Subject = subject;
                di.DeliverySystem = bulletin.DeliverySystem;
                di.Status = "To Be Processed";

                foreach (IBulletinSubscriber subscriber in bulletin.Subscribers)
                {
                    IDeliveryItemTarget target = EntityFactory.Create<IDeliveryItemTarget>();
                    target.DeliveryItem = di;
                    target.Address = subscriber.Subscriber.UserInfo.Email;
                    target.Type = "TO";
                    di.DeliveryItemTargets.Add(target);
                    di.Save();
                    Progress = 50M + (50M * ++counter / bulletin.Subscribers.Count);
                }
            }
        }

        private decimal GetQuotaAmtforUser(IUser user, DateTime today)
        {
            decimal amount = 0;

            if (user != null)
            {
                using (var session = new SessionScopeWrapper())
                {
                    IList<IQuota> quotas = session.QueryOver<IQuota>()
                        .Where(x => today >= x.BeginDate && today <= x.EndDate && x.IsActive == true && x.AssignedTo == user)
                        .List<IQuota>();

                    foreach (IQuota q in quotas)
                    {
                        amount += (decimal)q.Amount;
                    }
                }
            }

            return amount;
        }

        private string BuildMessageBody(ForecastSummary fs)
        {
            System.Text.StringBuilder body = new System.Text.StringBuilder();
            string rowMask = "<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td><td>{7}</td></tr>";

            // Start Table
            body.AppendLine("<body>");
            body.AppendLine("<table style=\"width: 90%; font-family: Arial, Helvetica, sans-serif; font-size: smaller;\" border=\"1\" cellpadding=\"1\" cellspacing=\"1\">");
            body.AppendLine("<tbody>");

            // Build Header
            body.AppendLine(String.Format(rowMask, 
                "<b>Forecast Name</b>",
                "<b>Assigned To</b>",
                "<b>Amount</b>",
                "<b>Pipeline</b>",
                "<b>Closed-Won</b>",
                "<b>Quota</b>",
                "<b>% To Forecast</b>",
                "<b>% To Quota</b>"
                ));

            foreach (ForecastInfo fi in fs.forecasts)
            {
                body.AppendLine(String.Format(rowMask,
                    fi.ForecastName,
                    fi.AssignedTo,
                    Decimal.Round(fi.Amount,0),
                    Decimal.Round(fi.Pipeline,0),
                    Decimal.Round(fi.ClosedWon,0),
                    Decimal.Round(fi.Quota, 0),
                    Decimal.Round(fi.PercentToForecast,2).ToString() + "%",
                    Decimal.Round(fi.PercentToQuota,2).ToString() + "%"
                    ));
            }

            // Build Footer
            body.AppendLine(String.Format(rowMask,
                "<b>Total</b>",
                "<b></b>",
                "<b>" + Decimal.Round(fs.AmountTotal,0) + "</b>",
                "<b>" + Decimal.Round(fs.PipelineTotal,0) + "</b>",
                "<b>" + Decimal.Round(fs.ClosedWonTotal,0) + "</b>",
                "<b>" + Decimal.Round(fs.QuotaTotal,0) + "</b>",
                "<b>" + Decimal.Round(fs.PercentToForecast,2).ToString() + "%" + "</b>",
                "<b>" + Decimal.Round(fs.PercentToQuota, 2).ToString() + "%" + "</b>"
                ));

            // End Table
            body.AppendLine("</tbody>");
            body.AppendLine("</table>");
            body.AppendLine("</body>");

            return body.ToString();
        }
    }

    class ForecastSummary
    {
        private int _count = 0;
        private decimal _amountTotal = 0;
        private decimal _pipelineTotal = 0;
        private decimal _quotaTotal = 0;
        private decimal _closedWonTotal = 0;

        public int Count
        {
            get
            {
                _count = 0;
                if (forecasts != null)
                    _count = forecasts.Count;

                return _count;
            }
        }

        public decimal AmountTotal
        {
            get
            {
                _amountTotal = 0;
                if (forecasts != null)
                {
                    foreach (ForecastInfo fi in forecasts)
                    {
                        _amountTotal += fi.Amount;
                    }
                }
                return _amountTotal;
            }
        }

        public decimal PipelineTotal
        {
            get
            {
                _pipelineTotal = 0;
                if (forecasts != null)
                {
                    foreach (ForecastInfo fi in forecasts)
                    {
                        _pipelineTotal += fi.Pipeline;
                    }
                }
                return _pipelineTotal;
            }
        }

        public decimal QuotaTotal
        {
            get
            {
                _quotaTotal = 0;
                if (forecasts != null)
                {
                    foreach (ForecastInfo fi in forecasts)
                    {
                        _quotaTotal += fi.Quota;
                    }
                }
                return _quotaTotal;
            }
        }

        public decimal ClosedWonTotal
        {
            get
            {
                _closedWonTotal = 0;
                if (forecasts != null)
                {
                    foreach (ForecastInfo fi in forecasts)
                    {
                        _closedWonTotal += fi.ClosedWon;
                    }
                }
                return _closedWonTotal;
            }
        }

        public decimal PercentToForecast
        {
            get
            {
                if (AmountTotal > 0)
                    return Decimal.Round((ClosedWonTotal / AmountTotal), 2);
                else
                    return 0;
            }
        }

        public decimal PercentToQuota
        {
            get
            {
                if (QuotaTotal > 0)
                    return Decimal.Round((ClosedWonTotal / QuotaTotal), 2);
                else
                    return 0;
            }
        }

        public IList<ForecastInfo> forecasts;

        public ForecastSummary()
        {
            forecasts = new List<ForecastInfo>();
        }
    }

    class ForecastInfo
    {
        private decimal _percentToForecast;
        private decimal _percentToQuota;

        public string ForecastName { get; set; }
        public string AssignedTo { get; set; }

        [System.ComponentModel.DefaultValue(0)]
        public decimal Amount { get; set; }

        [System.ComponentModel.DefaultValue(0)]
        public decimal Pipeline { get; set; }

        [System.ComponentModel.DefaultValue(0)]
        public decimal Quota { get; set; }

        [System.ComponentModel.DefaultValue(0)]
        public decimal ClosedWon { get; set; }
        public decimal PercentToForecast
        {
            get
            {
                if (Amount != 0 && ClosedWon != 0)
                {
                    return (ClosedWon / Amount);
                }
                else
                    return 0;
            }
        }
        public decimal PercentToQuota
        {
            get
            {
                if (ClosedWon != 0 && Quota != 0)
                {
                    return (ClosedWon / Amount);
                }
                else
                    return 0;
            }
        }
    }
}
