using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Exchange.WebServices.Data;

namespace SalesLogix.DeliverySystems
{
    public class ExchangeWebServices : Object, IDisposable
    {
        private ExchangeService _service = new ExchangeService(ExchangeVersion.Exchange2010_SP1);

        public ExchangeService Service
        { get { return _service; } }

        public ExchangeWebServices(NetworkCredential user, string ExchangeUrl)
        {
            try
            {
                _service.UseDefaultCredentials = false;
                _service.Credentials = user;
                //_service.PreAuthenticate = true;
                _service.Url = new Uri(ExchangeUrl);
                ServicePointManager.ServerCertificateValidationCallback += delegate(
                    object sender,
                    System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                    System.Security.Cryptography.X509Certificates.X509Chain chain,
                    System.Net.Security.SslPolicyErrors sslPolicyErrors)
                {
                    return true;
                };
            }
            catch (Exception ex)
            {
                string msg = String.Format("ExchangeWebServices\n{0}\n{1}", ex.Message, ex.StackTrace);
                System.Diagnostics.EventLog.WriteEntry("ExchangeWebServices", msg, System.Diagnostics.EventLogEntryType.Error);
            }
        }

        public void Dispose()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }

            _service = null;
        }

        ~ExchangeWebServices()
        {
            Dispose(true);
        }

        #region Private Methods

        private void EmptyFolder(WellKnownFolderName folderName)
        {
            try
            {
                ItemView view = new ItemView(1000);
                view.PropertySet = new PropertySet(BasePropertySet.IdOnly);
                FindItemsResults<Item> findResults = _service.FindItems(folderName, view);

                foreach (Item i in findResults)
                {
                    i.Delete(DeleteMode.HardDelete);
                }
            }
            catch { }
        }

        #endregion

        #region Public Methods

        public bool ActivityExists(string uniqueId, DateTime startDate, DateTime endDate)
        {
            bool result = false;

            CalendarView c = new CalendarView(startDate, endDate);
            List<Appointment> appts = _service.FindAppointments(WellKnownFolderName.Calendar, c).ToList<Appointment>();
            foreach (Appointment a in appts)
            {
                a.Load();
                if (a.Body.Text.Contains(uniqueId))
                {
                    // Match Found
                    result = true;
                    break;
                }
            }

            return result;
        }

        public bool FetchAppointmentbyUniqueId(string uniqueId, DateTime startDate, DateTime endDate, out Appointment appt)
        {
            bool result = false;
            appt = null;

            CalendarView c = new CalendarView(startDate, endDate);
            List<Appointment> appts = _service.FindAppointments(WellKnownFolderName.Calendar, c).ToList<Appointment>();
            foreach (Appointment a in appts)
            {
                a.Load();
                if (a.Body.Text.Contains(uniqueId))
                {
                    // Match Found
                    appt = a;
                    result = true;
                    break;
                }
            }

            return result;
        }

        public bool FetchAppointmentbyUniqueId(string uniqueId, out Appointment appt, int occurrance = 1)
        {
            bool result = false;
            appt = null;

            try
            {
                SearchFilter.SearchFilterCollection searchFilter = new SearchFilter.SearchFilterCollection();
                searchFilter.Add(new SearchFilter.ContainsSubstring(AppointmentSchema.Subject, uniqueId));
                ItemView view = new ItemView(500);
                view.PropertySet = new PropertySet(BasePropertySet.IdOnly, AppointmentSchema.Subject, AppointmentSchema.Start, AppointmentSchema.AppointmentType);
                FindItemsResults<Item> findResults = _service.FindItems(WellKnownFolderName.Calendar, searchFilter, view);

                if (findResults.Items.Count > 0)
                {
                    int index = occurrance - 1;
                    if (findResults.Items[index] == null)
                        index = 0;
                    appt = findResults.Items[index] as Appointment;
                    appt.Load();
                    result = true;
                }
            }
            catch (Exception ex)
            {
                string msg = String.Format("FetchAppintmentbyUniqueId\n{0}\n{1}", ex.Message, ex.StackTrace);
                System.Diagnostics.EventLog.WriteEntry("ExchangeWebServices", msg, System.Diagnostics.EventLogEntryType.Error);
            }

            return result;
        }

        public bool FetchMeetingRequestbyUniqueId(string uniqueId, out MeetingRequest mr)
        {
            bool result = false;
            mr = null;

            try
            {
                SearchFilter.SearchFilterCollection searchFilter = new SearchFilter.SearchFilterCollection();
                searchFilter.Add(new SearchFilter.ContainsSubstring(MeetingRequestSchema.Subject, uniqueId));
                ItemView view = new ItemView(500);
                //view.PropertySet = new PropertySet(BasePropertySet.IdOnly, MeetingRequestSchema.Subject, MeetingRequestSchema.Start);
                FindItemsResults<Item> findResults = _service.FindItems(WellKnownFolderName.Inbox, searchFilter, view);

                if (findResults.Items.Count > 0)
                {
                    mr = findResults.Items[0] as MeetingRequest;
                    //mr.Load();
                    result = true;
                }
            }
            catch (Exception ex)
            {
                string msg = String.Format("FetchMeetingRequestbyUniqueId\n{0}\n{1}", ex.Message, ex.StackTrace);
                System.Diagnostics.EventLog.WriteEntry("ExchangeWebServices", msg, System.Diagnostics.EventLogEntryType.Error);
            }


            return result;
        }

        //public void EmptyInbox()
        //{
        //    EmptyFolder(WellKnownFolderName.Inbox);
        //}

        //public void EmptyCalendar()
        //{
        //    EmptyFolder(WellKnownFolderName.Calendar);
        //}

        //public void EmptyTasks()
        //{
        //    EmptyFolder(WellKnownFolderName.Tasks);
        //}

        //public void EmptyContacts()
        //{
        //    EmptyFolder(WellKnownFolderName.Contacts);
        //}

        //public void EmptyDeletedItems()
        //{
        //    EmptyFolder(WellKnownFolderName.DeletedItems);
        //}

        //public void EmptySentItems()
        //{
        //    EmptyFolder(WellKnownFolderName.SentItems);
        //}

        //public void EmptyAllFolders()
        //{
        //    EmptyInbox();
        //    EmptyContacts();
        //    EmptyCalendar();
        //    EmptyTasks();
        //    EmptySentItems();
        //    EmptyDeletedItems();
        //}

        #endregion
    }
}
