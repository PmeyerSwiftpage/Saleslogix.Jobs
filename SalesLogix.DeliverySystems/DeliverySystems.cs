using System;
using Sage.Entity.Interfaces;
using System.Net;
using System.Net.Mail;
using Microsoft.Exchange.WebServices.Data;

namespace SalesLogix.DeliverySystems
{
    public struct DeliveryItemStatuses
    {
        public const string ToBeProcessed = "To Be Processed";
        public const string InProcess = "In Process";
        public const string Completed = "Completed";
        public const string Error = "Error Occurred";
    }

    public struct DeliverySystemTypes
    {
        public const string Smtp = "SMTP";
        public const string Exch = "Exchange";
        public const string Sms = "SMS";
    }

    public struct DeliveryItemTargetTypes
    {
        public const string ReplyTo = "Reply To";
        public const string To = "To";
        public const string Cc = "Cc";
        public const string Bcc = "Bcc";
    }

    public class DeliverySystem
    {
        public void Send(IDeliveryItem di)
        {
            bool result = false;
            string errorMsg = null;

            di.Status = DeliveryItemStatuses.InProcess;
            di.Save();

            switch (di.DeliverySystem.SystemType)
            {
                case DeliverySystemTypes.Smtp:
                    result = SendSmtp(di, out errorMsg);
                    break;
                case DeliverySystemTypes.Exch:
                    result = SendExch(di, out errorMsg);
                    break;
                case DeliverySystemTypes.Sms:
                    result = SendSms(di, out errorMsg);
                    break;
            }

            if (result)
            {
                di.Status = DeliveryItemStatuses.Completed;
                di.ErrorText = null;
                di.CompletedDate = DateTime.Now;
                di.Save();
            }
            else
            {
                di.Status = DeliveryItemStatuses.Error;
                di.ErrorText = errorMsg;
                di.Save();
            }
        }

        private bool SendSmtp(IDeliveryItem di, out string errorMsg)
        {
            bool result = false;
            errorMsg = null;

            try
            {
                // Create a blank email message            
                MailMessage email = new MailMessage();

                // Set the proper properties based on the deliverySystem
                email.From = new MailAddress(di.DeliverySystem.EmailAddress);
                email.Sender = email.From;
                email.IsBodyHtml = Convert.ToBoolean(di.DeliverySystem.SmtpIsBodyHtml);

                // Add Targets
                foreach (IDeliveryItemTarget target in di.DeliveryItemTargets)
                {
                    switch (target.Type.ToUpper())
                    {
                        case "TO":
                            email.To.Add(new MailAddress(target.Address));
                            break;
                        case "CC":
                            email.CC.Add(new MailAddress(target.Address));
                            break;
                        case "BCC":
                            email.Bcc.Add(new MailAddress(target.Address));
                            break;
                    }
                }

                // Add Subject
                email.Subject = di.Subject;

                // Add Body
                email.Body = di.Body;

                // Send via Smtp
                SmtpClient smtp = new SmtpClient(di.DeliverySystem.ServerAddress, Convert.ToInt16(di.DeliverySystem.SmtpPort));
                smtp.EnableSsl = Convert.ToBoolean(di.DeliverySystem.SmtpEnableSsl);
                smtp.Credentials = new System.Net.NetworkCredential(di.DeliverySystem.UserName, di.DeliverySystem.UserPassword);
                smtp.Send(email);
                result = true;
            }
            catch (Exception ex)
            {
                errorMsg = String.Format("Send Smtp - Error\n{0}\n{1}", ex.Message, ex.StackTrace);
            }

            return result;
        }

        private bool SendExch(IDeliveryItem di, out string errorMsg)
        {
            bool result = false;
            errorMsg = null;

            try
            {
                NetworkCredential creds = new NetworkCredential();
                creds.UserName = di.DeliverySystem.UserName;
                creds.Domain = di.DeliverySystem.UserDomain;
                creds.Password = di.DeliverySystem.UserPassword;

                using (ExchangeWebServices _exchange = new ExchangeWebServices(creds, di.DeliverySystem.ServerAddress))
                {
                    EmailMessage message = new EmailMessage(_exchange.Service);
                    message.Subject = di.Subject;
                    message.Body = di.Body;
                    message.From = di.DeliverySystem.EmailAddress;

                    foreach (IDeliveryItemTarget target in di.DeliveryItemTargets)
                    {
                        switch (target.Type)
                        {
                            case DeliveryItemTargetTypes.To:
                                message.ToRecipients.Add(new EmailAddress(target.Address));
                                break;
                            case DeliveryItemTargetTypes.Cc:
                                message.CcRecipients.Add(new EmailAddress(target.Address));
                                break;
                            case DeliveryItemTargetTypes.Bcc:
                                message.BccRecipients.Add(new EmailAddress(target.Address));
                                break;
                        }
                    }

                    if (message.ToRecipients.Count > 0)
                    {
                        message.Send();
                    }
                }

                result = true;
            }
            catch (Exception ex)
            {
                errorMsg = String.Format("Send Exchange - Error\n{0}\n{1}", ex.Message, ex.StackTrace);
            }

            return result;
        }

        private bool SendSms(IDeliveryItem di, out string errorMsg)
        {
            bool result = false;
            errorMsg = "Not Implemented";

            return result;
        }
    }
}
