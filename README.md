Saleslogix.Jobs
===============

Examples of Jobs for the Job Service



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

