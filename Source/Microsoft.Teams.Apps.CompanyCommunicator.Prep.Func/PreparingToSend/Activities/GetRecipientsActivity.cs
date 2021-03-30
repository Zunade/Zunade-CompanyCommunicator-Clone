﻿// <copyright file="GetRecipientsActivity.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Prep.Func.PreparingToSend
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Table;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.DurableTask;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.NotificationData;
    using Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.SentNotificationData;

    /// <summary>
    /// Reads all the recipients from Sent notification table.
    /// </summary>
    public class GetRecipientsActivity
    {
        private readonly SentNotificationDataRepository sentNotificationDataRepository;
        private readonly int maxResultSize = 100000;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetRecipientsActivity"/> class.
        /// </summary>
        /// <param name="sentNotificationDataRepository">The sent notification data repository.</param>
        public GetRecipientsActivity(SentNotificationDataRepository sentNotificationDataRepository)
        {
            this.sentNotificationDataRepository = sentNotificationDataRepository ?? throw new ArgumentNullException(nameof(sentNotificationDataRepository));
        }

        /// <summary>
        /// Reads all the recipients from Sent notification table.
        /// </summary>
        /// <param name="notification">notification.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [FunctionName(FunctionNames.GetRecipientsActivity)]
        public async Task<(IEnumerable<SentNotificationDataEntity>, TableContinuationToken)> GetRecipientsAsync([ActivityTrigger] NotificationDataEntity notification)
        {
            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }

            var results = await this.sentNotificationDataRepository.GetByCountAsync(notification.Id, 1000);
            var recipients = new List<SentNotificationDataEntity>();
            recipients.AddRange(results.Item1);
            while (results.Item2 != null && recipients.Count < this.maxResultSize)
            {
                results = await this.sentNotificationDataRepository.GetByTokenAsync(results.Item2, notification.Id);
                if (results.Item1 != null && results.Item1.Count() > 0)
                {
                    recipients.AddRange(results.Item1);
                }
            }

            return (recipients, results.Item2);
        }

        /// <summary>
        /// Reads all the recipients from Sent notification table.
        /// </summary>
        /// <param name="input">Input containing notification id and continuation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [FunctionName(FunctionNames.GetRecipientsByTokenActivity)]
        public async Task<(IEnumerable<SentNotificationDataEntity>, TableContinuationToken)> GetRecipientsByTokenAsync(
            [ActivityTrigger](string notificationId, TableContinuationToken tableContinuationToken) input)
        {
            if (input.notificationId == null)
            {
                throw new ArgumentNullException(nameof(input.notificationId));
            }

            if (input.tableContinuationToken == null)
            {
                throw new ArgumentNullException(nameof(input.tableContinuationToken));
            }

            var recipients = new List<SentNotificationDataEntity>();
            while (input.tableContinuationToken != null && recipients.Count < this.maxResultSize)
            {
                var results = await this.sentNotificationDataRepository.GetByTokenAsync(input.tableContinuationToken, input.notificationId);
                if (results.Item1 != null && results.Item1.Count() > 0)
                {
                    recipients.AddRange(results.Item1);
                }

                input.tableContinuationToken = results.Item2;
            }

            return (recipients, input.tableContinuationToken);
        }

        /// <summary>
        /// Reads all the recipients from Sent notification table who do not have conversation details.
        /// </summary>
        /// <param name="notification">notification.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        [FunctionName(FunctionNames.GetPendingRecipientsActivity)]
        public async Task<IEnumerable<SentNotificationDataEntity>> GetPendingRecipientsAsync([ActivityTrigger] NotificationDataEntity notification)
        {
            var recipients = await this.sentNotificationDataRepository.GetAllAsync(notification.Id);
            return recipients.Where(recipient => string.IsNullOrEmpty(recipient.ConversationId));
        }
    }
}
