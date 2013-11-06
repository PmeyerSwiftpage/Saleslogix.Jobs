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
using SalesLogix.Jobs.Forecast.Localization;
using SalesLogix.Jobs.Forecast.Properties;

namespace SalesLogix.Jobs.Forecast
{
    using SalesLogix.FiscalAPI;

    [DisallowConcurrentExecution]
    [SRDisplayName(SR.Job_SnapshotForecasts_DisplayName)]
    [SRDescription(SR.Job_SnapshotForecasts_Description)]
    public class SnapshotForecasts : SystemJobBase
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool _interrupted = false;
        protected override void OnInterrupt()
        {
            base.OnInterrupt();
            _interrupted = Interrupted;
        }

        protected override void OnExecute()
        {
            Phase = Resources.Job_Phase_Detail_Initializing;

            // Set progress bar to 0
            Progress = 0;
            decimal count = 0;

            // Get Current Date
            DateTime today = DateTime.Now.Date;

            // Retrieve all Forecasts for the current period
            // Cycle through them and create a snaphsot for each one.
            using (var session = new SessionScopeWrapper(true))
            {
                Phase = Resources.Job_SnapshotForecasts_Phase_Retrieving;
                IList<IForecast> forecasts = session.QueryOver<IForecast>()
                    .Where(x => today >= x.BeginDate && today <= x.EndDate && x.Active == true)
                    .List<IForecast>();

                // Cycle through each forecast and create a snapshot
                Phase = Resources.Job_SnapshotForecasts_Phase_TakingSnapshots;
                foreach (IForecast forecast in forecasts)
                {
                    forecast.TakeSnapshot();
                    Progress = (100M * ++count / forecasts.Count);
                    if (Interrupted) return;
                }
                Phase = Resources.Job_Phase_Detail_Finalizing;
            }

            // Set progress bar to 100 (finished)
            Progress = 100;
            Phase = Resources.Job_Phase_Detail_Completed;
            PhaseDetail = String.Format(Resources.Job_SnapshotForecasts_Results, count);
        }
    }

}
