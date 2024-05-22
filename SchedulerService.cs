internal async Task<bool> ShouldScheduleAsync(RepositoryConfiguration repoConfig)
{
    try
    {
        RepositoryMetadata repoMetadata = await schedulerHelper.HandleRepositoryMetadataAsync(
            repoConfig.OrganizationName, repoConfig.RepositoryId
        ).ConfigureAwait(false);
        DateTimeOffset? lastCollectionEndDate = repoMetadata.LastCollectionEndDate;

        // Immediately schedule the repository if it has never been scheduled before
        if (!lastCollectionEndDate.HasValue)
        {
            return true;
        }

        if (repoConfig.RepositorySchedule.Type == "Cron")
        {
            string cronSchedule = repoConfig.RepositorySchedule.Value;
            CrontabSchedule schedule;
            try
            {
                schedule = CrontabSchedule.Parse(cronSchedule);
            }
            catch (CrontabException e)
            {
                telemetryClient.TrackException(e, "Cannot parse cron schedule.");
                return false;
            }

            DateTime nextScheduledTime = schedule.GetNextOccurrence(lastCollectionEndDate.Value.UtcDateTime);
            // Only schedule repository if the current time is greater than or equal to the next scheduled time
            return timeProvider.GetUtcNow() >= nextScheduledTime;
        }
        else
        {
            // Currently not handling other types of schedules
            return false;
        }
    }
    catch (Exception e)
    {
        telemetryClient.TrackException(e, "Error in ShouldScheduleAsync");
        throw;
    }
}
