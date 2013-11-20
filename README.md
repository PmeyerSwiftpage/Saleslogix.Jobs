Saleslogix.Jobs
===============

Examples of Jobs for the Job Service

Job Descriptions
----------------

* Delivery.ProcessDeliveryItems - A basic email "Delivery" system (server-side).  The Job polls periodically items in the DELIVERYITEM
table and processes them.
* Forecast.SnapshotForecasts - Every Saturday at 12AM, take a snapshot of all Active Forecasts for the current period.
* Forecast.OpportunityRollover - Every 1st of the month at 12AM, move all "Open" Opportunities from the previous
period to the first day of the new period.  Then create new Forecasts for the sales people that are a part of the
"Quota & Forecast Users" Role.
* Notification.ProcessNotifications - Handles the processing of Notification Events.  Builds up emails to be delivered, and uses the Delivery System (and job) to handle the delivery of the emails.
* Bulletin.ProcessBulletins - Handles the processing of Bulletins -- sort of like a Notification Event, but people can "Subscribe" to a bulletin.


Notification Event
------------------
web.config change needed to allow the saving of HMTL in a field:

	<location path="InsertNotificationEvent.aspx">
		<system.web>
			<httpRuntime requestValidationMode="2.0" />
		</system.web>
	</location>
	<location path="NotificationEvent.aspx">
		<system.web>
			<httpRuntime requestValidationMode="2.0" />
		</system.web>
	</location>

