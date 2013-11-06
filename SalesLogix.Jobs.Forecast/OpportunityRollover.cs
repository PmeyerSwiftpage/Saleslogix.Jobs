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

    /// <summary>
    /// OpportunityRollover
    /// </summary>
    [DisallowConcurrentExecution]
    [SRDisplayName(SR.Job_OpportunityRollover_DisplayName)]
    [SRDescription(SR.Job_OpportunityRollover_Description)]
    public class OpportunityRollover : SystemJobBase
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
            int count = 0;

            // Retrieve all Users in the Role defined for Quota & Forecast Users
            using (var session = new SessionScopeWrapper(true))
            {
                Phase = Resources.Job_OpportunityRollover_Phase_Retrieving_Users;
                IRole role = session.QueryOver<IRole>()
                    .Where(x => x.RoleName == Resources.Job_OpportunityRollover_RoleName)
                    .SingleOrDefault<IRole>();

                foreach (IUserRole userRole in role.Users)
                {
                    RolloverOpportunities(userRole.User);
                    GenerateForecast(userRole.User);
                    Progress = (100M * ++count / role.Users.Count);
                    if (Interrupted) return;
                }
            }
            Progress = 100;
            Phase = Resources.Job_Phase_Detail_Completed;
            PhaseDetail = String.Format(Resources.Job_OpportunityRollover_Results, count);
        }

        private void RolloverOpportunities(IUser user)
        {
            // Get Current Date
            DateTime today = DateTime.Now.Date;

            // Get Fiscal Year Settings
            int period = Platform.GetQAndFPeriod();
            DateTime firstDayofPeriod = (DateTime)Platform.FirstDayofPeriod(DateTime.Now);
            DateTime beginPreviousPeriod = (DateTime)Platform.FirstDayofPeriod(today.AddMonths(-1 * period)).Value.Date;
            DateTime endPreviousPeriod = Date.EndOfDay((DateTime)Platform.LastDayofPeriod(beginPreviousPeriod));

            using (var session = new SessionScopeWrapper(true))
            {
                Phase = String.Format(Resources.Job_OpportunityRollover_Phase_ProcessingOpportunities, user.UserInfo.NameLF);
                // Get the Opportunities that need to be rolled over from the previous period
                IList<IOpportunity> opportunities = session.QueryOver<IOpportunity>()
                    .Where(x => x.AccountManager == user && x.Status == "Open" && x.AddToForecast == true && x.EstimatedClose >= beginPreviousPeriod && x.EstimatedClose <= endPreviousPeriod)
                    .List<IOpportunity>();
                foreach (IOpportunity opportunity in opportunities)
                {
                    opportunity.EstimatedClose = firstDayofPeriod;
                    opportunity.Save();
                }
            }
        }

        private void GenerateForecast(IUser user)
        {
            IForecast forecast = Sage.Platform.EntityFactory.Create<IForecast>();
            forecast.AssignedTo = user;
            forecast.BeginDate = (DateTime)Platform.FirstDayofPeriod(DateTime.Now);
            forecast.EndDate = (DateTime)Platform.LastDayofPeriod(forecast.BeginDate);
            forecast.Description = forecast.GetFormattedDescription();
            forecast.Active = true;
            forecast.Save();

            // for some reason, need to requery the record that was just saved.
            forecast = Sage.Platform.EntityFactory.GetById<IForecast>(forecast.Id.ToString());
            forecast.PullInOpportunities();
            forecast.Amount = Convert.ToDecimal(forecast.PeriodClosedWonAmt() + forecast.PeriodPipelineAmt("Avg"));
            forecast.Save();
        }
    }
}
