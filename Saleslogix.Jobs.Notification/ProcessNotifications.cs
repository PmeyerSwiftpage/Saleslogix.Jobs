using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Sage.Entity.Interfaces;
using Sage.Platform;
using Sage.Platform.Orm;
using Sage.Platform.Scheduling;
using Quartz;
using log4net;
using SalesLogix.Jobs.Notification.Localization;

namespace SalesLogix.Jobs.Notification
{

    public struct QueryLiterals
    {
        public const string LastChecked = ":LastChecked";
        public const string Today = ":Today";
        public const string Yesterday = ":Yesterday";
        public const string Tomorrow = ":Tomorrow";
    }

    [DisallowConcurrentExecution]
    [SRDisplayName(SR.Job_ProcessNotifications_DisplayName)]
    [SRDescription(SR.Job_ProcessNotifications_Description)]
    public class ProcessNotifications : SystemJobBase
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool _interrupted = false;
        private Dictionary<string, Type> _entityTypes = new Dictionary<string, Type>();
        protected override void OnInterrupt()
        {
            base.OnInterrupt();
            _interrupted = Interrupted;
        }

        protected override void OnExecute()
        {
            IList<INotificationEvent> notificationEvents = null;

            // Get the list of Notification Events to process
            using (var session = new SessionScopeWrapper())
            {
                notificationEvents = session.QueryOver<INotificationEvent>()
                    .Where(x => x.NextTimeToCheck <= DateTime.Now && x.Enabled == true)
                    .List<INotificationEvent>();
            }

            if (notificationEvents != null)
            {
                foreach (INotificationEvent ne in notificationEvents)
                {
                    string query = ParseQueryForLiterals(ne);
                    DateTime now = DateTime.UtcNow;
                    bool bResult = false;

                    // Execute the query
                    using (var session = new SessionScopeWrapper())
                    {
                        Type entityType = typeof(Sage.SalesLogix.Entities.Account).Assembly.GetType(String.Format("Sage.SalesLogix.Entities.{0}", ne.EntityName));

                        IList<dynamic> payload = session.CreateQuery(query).List<dynamic>();

                        if (payload.Count > 0)
                        {
                            if (ne.Digest == true)
                            {
                                bResult = ProcessDigestEvent(ne, payload);
                            }
                            else
                            {
                                bResult = ProcessSingleEvent(ne, payload);
                            }
                        }
                        else
                        {
                            // Noting to process, but move the next time to check anyway.
                            bResult = true;
                        }
                    }
                    if (bResult)
                    {
                        // Update Notification Event
                        ne.LastChecked = now;
                        ne.NextTimeToCheck = GetNextTimeToCheck(ne, now);
                        ne.Save();
                    }
                }
            }
        }

        #region Private Methods

        private bool ProcessDigestEventTargetList(INotificationEvent ne, IList<dynamic> payload)
        {
            bool bResult = false;
            int itemCount = 0;
            IDeliveryItem deliveryItem = EntityFactory.Create<IDeliveryItem>();
            deliveryItem.DeliverySystem = ne.DeliveryMethod;
            deliveryItem.Status = DeliverySystems.DeliveryItemStatuses.ToBeProcessed;

            try
            {
                // Build Body
                foreach (dynamic item in payload)
                {
                    string body = null;
                    body = GenerateBody(item, ne.EmailTemplate);
                    deliveryItem.Body = String.Format("{0}{1}", deliveryItem.Body, body);
                    itemCount++;
                }

                // Build Subject
                deliveryItem.Subject = GenerateSubject(ne, itemCount);

                // Add Targets
                foreach (INotificationTarget target in ne.Targets)
                {
                    if (target.Target.Type == OwnerType.User)
                    {
                        IDeliveryItemTarget t = EntityFactory.Create<IDeliveryItemTarget>();
                        t.Type = DeliverySystems.DeliveryItemTargetTypes.To;
                        t.Address = target.Target.User.UserInfo.Email;
                        t.DeliveryItem = deliveryItem;
                        deliveryItem.DeliveryItemTargets.Add(t);
                    }
                    else
                    {
                        // Get 1st level of Team or Dept members
                        IList<string> emails = GetEmails(target.Target);

                        foreach (string email in emails)
                        {
                            IDeliveryItemTarget t = EntityFactory.Create<IDeliveryItemTarget>();
                            t.Type = "TO";
                            t.Address = email;
                            t.DeliveryItem = deliveryItem;
                            deliveryItem.DeliveryItemTargets.Add(t);
                        }
                    }
                }
                deliveryItem.Save();
                bResult = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
            return bResult;
        }

        private bool ProcessDigestEventDynamicTargetList(INotificationEvent ne,IList<dynamic> payload)
        {
            bool bResult = false;
            IList<string> currentEmails = new List<string>();
            int itemCount = 0;
            string body = null;
            string currentTargetName = null;

            try
            {
                foreach (dynamic item in payload)
                {
                    IList<string> theseEmails = new List<string>();
                    string thisTargetName = null;

                    // Get the Dynamic Field to send to
                    dynamic dynamicField = item.GetType().GetProperty(ne.DynamicNotificationField).GetValue(item, null);

                    if (dynamicField is IContact)
                    {
                        theseEmails.Add(dynamicField.GetType().GetProperty("Email").GetValue(dynamicField, null));
                        thisTargetName = dynamicField.GetType().GetProperty("FullName").GetValue(dynamicField, null);
                    }
                    else if (dynamicField is IUser)
                    {
                        theseEmails.Add(dynamicField.GetType().GetProperty("Email").GetValue(dynamicField, null));
                        thisTargetName = dynamicField.GetType().GetProperty("UserName").GetValue(dynamicField, null);
                    }
                    else if (dynamicField is IOwner)
                    {
                        theseEmails = GetEmails(dynamicField);
                        thisTargetName = dynamicField.GetType().GetProperty("OwnerDescription").GetValue(dynamicField, null);
                    }

                    if (currentTargetName == null || (String.Compare(currentTargetName, thisTargetName) != 0))
                    {
                        // Build up message body
                        string tempBody = GenerateBody(item, ne.EmailTemplate);
                        body = String.Format("{0}{1}", body, tempBody);
                        itemCount++;
                    }
                    else
                    {
                        // Deliver current one
                        CreateDeliveryItem(GenerateSubject(ne, itemCount, currentTargetName), body, ne.DeliveryMethod, currentEmails);

                        // Set up new one
                        currentTargetName = thisTargetName;
                        currentEmails = theseEmails;
                        body = GenerateBody(item, ne.EmailTemplate);
                        itemCount = 1;
                    }
                }
                bResult = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }

            // Send last one
            CreateDeliveryItem(GenerateSubject(ne, itemCount, currentTargetName), body, ne.DeliveryMethod, currentEmails);
            return bResult;
        }

        private bool ProcessDigestEvent(INotificationEvent ne, IList<dynamic> payload)
        {
            if (ne.DynamicNotification == false)
            {
                return ProcessDigestEventTargetList(ne, payload);
            }
            else
            {
                return ProcessDigestEventDynamicTargetList(ne, payload);
            }
        }

        private bool ProcessSingleEvent(INotificationEvent ne, IList<dynamic> payload)
        {
            bool bResult = false;

            try
            {
                foreach (dynamic item in payload)
                {
                    IList<string> targets = new List<string>();

                    if (ne.Digest == false)
                    {
                        // Add Targets
                        foreach (INotificationTarget target in ne.Targets)
                        {
                            if (target.Target.Type == OwnerType.User)
                            {
                                targets.Add(target.Target.User.UserInfo.Email);
                            }
                            else
                            {
                                // Get 1st level of Team or Dept members
                                targets = GetEmails(target.Target);
                            }
                        }
                        CreateDeliveryItem(GenerateSubject(ne, 1), GenerateBody(item, ne.EmailTemplate), ne.DeliveryMethod, targets);
                    }
                    else
                    {
                        string targetName = null;

                        // Get the Dynamic Field to send to
                        dynamic dynamicField = item.GetType().GetProperty(ne.DynamicNotificationField).GetValue(item, null);

                        if (dynamicField is IContact)
                        {
                            targets.Add(dynamicField.GetType().GetProperty("Email").GetValue(dynamicField, null));
                            targetName = dynamicField.GetType().GetProperty("FullName").GetValue(dynamicField, null);
                        }
                        else if (dynamicField is IUser)
                        {
                            targets.Add(dynamicField.GetType().GetProperty("Email").GetValue(dynamicField, null));
                            targetName = dynamicField.GetType().GetProperty("UserName").GetValue(dynamicField, null);
                        }
                        else if (dynamicField is IOwner)
                        {
                            targets = GetEmails(dynamicField);
                            targetName = dynamicField.GetType().GetProperty("OwnerDescription").GetValue(dynamicField, null);
                        }

                        CreateDeliveryItem(GenerateSubject(ne, 1, targetName), GenerateBody(item, ne.EmailTemplate), ne.DeliveryMethod, targets);
                    }
                }
                bResult = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
            return bResult;
        }

        public void CreateDeliveryItem(string subject, string body, IDeliverySystem ds, IList<string> targets)
        {
            IDeliveryItem deliveryItem = EntityFactory.Create<IDeliveryItem>();
            deliveryItem.Subject = subject;
            deliveryItem.Body = body;
            deliveryItem.Status = DeliverySystems.DeliveryItemStatuses.ToBeProcessed;
            deliveryItem.DeliverySystem = ds;

            foreach (string target in targets)
            {
                IDeliveryItemTarget t = EntityFactory.Create<IDeliveryItemTarget>();
                t.Address = target;
                t.Type = DeliverySystems.DeliveryItemTargetTypes.To;
                deliveryItem.DeliveryItemTargets.Add(t);
            }
            deliveryItem.Save();
        }

        private static List<DayOfWeek> Days(INotificationEvent notificationEvent)
        {
            List<DayOfWeek> result = new List<DayOfWeek>();

            // I'm sure there is a better way of doing this.  Just hacking it in for now.
            if (notificationEvent.DaysOfWeek.Contains("Sun"))
                result.Add(DayOfWeek.Sunday);
            if (notificationEvent.DaysOfWeek.Contains("Mon"))
                result.Add(DayOfWeek.Monday);
            if (notificationEvent.DaysOfWeek.Contains("Tues"))
                result.Add(DayOfWeek.Tuesday);
            if (notificationEvent.DaysOfWeek.Contains("Wed"))
                result.Add(DayOfWeek.Wednesday);
            if (notificationEvent.DaysOfWeek.Contains("Thurs"))
                result.Add(DayOfWeek.Thursday);
            if (notificationEvent.DaysOfWeek.Contains("Fri"))
                result.Add(DayOfWeek.Friday);
            if (notificationEvent.DaysOfWeek.Contains("Sat"))
                result.Add(DayOfWeek.Saturday);

            return result;
        }

        private string GenerateBody(dynamic item, string template)
        {
            string body = template;

            Regex regex = new Regex("(<%.*%>)");
            MatchCollection v = regex.Matches(template);
            foreach (Match match in v)
            {
                if (!match.Value.Contains(':'))
                {
                    string property = match.Value.Replace("<%", "").Replace("%>", "");
                    if (item.GetType().GetProperty(property) != null)
                    {
                        var value = item.GetType().GetProperty(property).GetValue(item, null);

                        if (value != null)
                        {
                            body = body.Replace(String.Format("<%{0}%>", match.Value), value.ToString());
                        }
                    }
                }
            }
            return body;
        }

        private string GenerateSubject(INotificationEvent ne, int itemCount, string name = null)
        {
            string subject = ne.EmailSubjectTemplate;

            string entityName = ne.EntityName;
            string isare = "is";
            string hashave = "has";
            if (itemCount > 1)
            {
                isare = "are";
                hashave = "have";
                if (entityName.EndsWith("s"))
                    entityName = entityName + "es";
                else
                    entityName = entityName + "s";
            }

            try
            {
                subject = subject.Replace("<%:ISARE%>", isare);
                subject = subject.Replace("<%:NOW%>", DateTime.Now.ToShortDateString());
                subject = subject.Replace("<%:TODAY%>", DateTime.Today.ToShortDateString());
                subject = subject.Replace("<%:ITEMCOUNT%>", itemCount.ToString());
                subject = subject.Replace("<%:NAME%>", name);
                subject = subject.Replace("<%:ENTITY%>", entityName);
                subject = subject.Replace("<%:HASHAVE%>", hashave);
            }
            catch { }

            return subject;
        }

        private IList<string> GetEmails(IOwner owner)
        {
            IList<string> emails = new List<string>();

            if (owner.Type == OwnerType.Department)
            {
                IDepartment d = EntityFactory.GetById<IDepartment>(owner.Id.ToString());
                IList<IOwnerJoin> members = d.GetMembers();
                foreach (IOwnerJoin oj in members)
                {
                    if (oj.Child.Type == OwnerType.User)
                    {
                        if (!String.IsNullOrEmpty(oj.Child.User.UserInfo.Email))
                        {
                            emails.Add(oj.Child.User.UserInfo.Email);
                        }
                    }
                }
            }
            return emails;
        }

        private DateTime GetNextTimeToCheck(INotificationEvent ne, DateTime fromTime)
        {
            DateTime nextCheck = fromTime;
            List<DayOfWeek> days = Days(ne);

            switch (ne.CheckIntervalType)
            {
                case "Timer":
                    nextCheck = nextCheck.AddMinutes((int)ne.CheckInterval);
                    while (!days.Contains(nextCheck.DayOfWeek))
                    {
                        nextCheck = nextCheck.AddDays(1);
                    }
                    break;
                case "Daily":
                    TimeSpan x = ne.TimeOfDayToCheck.Value - ne.TimeOfDayToCheck.Value.Date;
                    nextCheck = nextCheck.Date + x;
                    while (!days.Contains(nextCheck.DayOfWeek))
                    {
                        nextCheck = nextCheck.AddDays(1);
                    }
                    break;
            }
            return nextCheck;
        }

        private string ParseQueryForLiterals(INotificationEvent ne)
        {
            string query = ne.Query;

            if (!String.IsNullOrEmpty(query))
            {
                query = query.Replace(QueryLiterals.LastChecked, "'" + ne.LastChecked.Value.ToString() + "'");
                query = query.Replace(QueryLiterals.Today, "'" + DateTime.UtcNow.Date + "'");
                query = query.Replace(QueryLiterals.Tomorrow, "'" + DateTime.UtcNow.Date.AddDays(1) + "'");
                query = query.Replace(QueryLiterals.Yesterday, "'" + DateTime.UtcNow.Date.AddDays(-1) + "'");
            }

            return query;
        }

        #endregion
    }
}
