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
using SalesLogix.Jobs.Delivery.Localization;
using SalesLogix.Jobs.Delivery.Properties;
using SalesLogix.DeliverySystems;

namespace SalesLogix.Jobs.Delivery
{
    [DisallowConcurrentExecution]
    [SRDisplayName(SR.Job_ProcessDeliveryItems_DisplayName)]
    [SRDescription(SR.Job_ProcessDeliveryItems_Description)]
    public class ProcessDeliveryItems : SystemJobBase
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
            Progress = 0;
            using (var session = new SessionScopeWrapper())
            {
                Phase = "Gathering up Items to deliver";
                IList<IDeliveryItem> deliveryItems = session.QueryOver<IDeliveryItem>()
                    .Where(di => di.Status == DeliveryItemStatuses.ToBeProcessed)
                    .List<IDeliveryItem>();

                if (deliveryItems != null)
                {
                    decimal counter = 0;
                    Phase = "Delivering Items";
                    foreach (IDeliveryItem di in deliveryItems)
                    {
                        ProcessDeliveryItem(di);
                        Progress = 100M * ++counter / deliveryItems.Count;
                        if (Interrupted) return;
                    }
                }
            }
            Progress = 100;
            Phase = Resources.Job_Phase_Detail_Completed;
        }

        private void ProcessDeliveryItem(IDeliveryItem deliveryItem)
        {
            DeliverySystem ds = new DeliverySystem();
            ds.Send(deliveryItem);
        }
    }
}
