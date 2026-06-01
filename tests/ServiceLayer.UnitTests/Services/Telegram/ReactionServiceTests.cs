using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceLayer.Services.Telegram;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace ServiceLayer.UnitTests.Services.Telegram
{
    public class ReactionServiceTests
    {
        private readonly Mock<IRepository<MessageReaction>> _reactionRepoMock = new();
        private readonly Mock<ILogger<ReactionService>> _loggerMock = new();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly ReactionService _service;

        public ReactionServiceTests()
        {
            _service = new ReactionService(_serviceProviderMock.Object, _loggerMock.Object, _reactionRepoMock.Object);
        }

        [Fact]
        public async Task ToggleReaction_NewReaction_ShouldAdd()
        {
            // Arrange
            long chatId = 1;
            long messageId = 100;
            long userId = 500;
            var type = MessageReactionType.Like;

            _reactionRepoMock.Setup(r => r.Get(It.IsAny<Expression<Func<MessageReaction, bool>>>()))
                .ReturnsAsync((MessageReaction)null);

            // Act
            await _service.ToggleReaction(chatId, messageId, userId, type);

            // Assert
            _reactionRepoMock.Verify(r => r.Add(It.Is<MessageReaction>(m => 
                m.ChatId == chatId && m.MessageId == messageId && m.UserId == userId && m.Reaction == type)), Times.Once);
            _reactionRepoMock.Verify(r => r.SaveChanges(), Times.Once);
        }

        [Fact]
        public async Task ToggleReaction_SameReaction_ShouldRemove()
        {
            // Arrange
            long chatId = 1;
            long messageId = 100;
            long userId = 500;
            var type = MessageReactionType.Like;
            var existing = new MessageReaction { ChatId = chatId, MessageId = messageId, UserId = userId, Reaction = type };

            _reactionRepoMock.Setup(r => r.Get(It.IsAny<Expression<Func<MessageReaction, bool>>>()))
                .ReturnsAsync(existing);

            // Act
            await _service.ToggleReaction(chatId, messageId, userId, type);

            // Assert
            Assert.Equal(MessageReactionType.None, existing.Reaction);
            _reactionRepoMock.Verify(r => r.Update(existing), Times.Once);
            _reactionRepoMock.Verify(r => r.SaveChanges(), Times.Once);
        }

        [Fact]
        public async Task ToggleReaction_MutuallyExclusive_ShouldReplace()
        {
            // Arrange
            long chatId = 1;
            long messageId = 100;
            long userId = 500;
            var existing = new MessageReaction { ChatId = chatId, MessageId = messageId, UserId = userId, Reaction = MessageReactionType.Like };

            _reactionRepoMock.Setup(r => r.Get(It.IsAny<Expression<Func<MessageReaction, bool>>>()))
                .ReturnsAsync(existing);

            // Act: Click Dislike when Like exists
            await _service.ToggleReaction(chatId, messageId, userId, MessageReactionType.Dislike);

            // Assert
            Assert.Equal(MessageReactionType.Dislike, existing.Reaction);
            _reactionRepoMock.Verify(r => r.Update(existing), Times.Once);
        }

        [Fact]
        public async Task ToggleReaction_NonMutuallyExclusive_ShouldKeepBoth()
        {
             // Arrange
            long chatId = 1;
            long messageId = 100;
            long userId = 500;
            var existing = new MessageReaction { ChatId = chatId, MessageId = messageId, UserId = userId, Reaction = MessageReactionType.Like };

            _reactionRepoMock.Setup(r => r.Get(It.IsAny<Expression<Func<MessageReaction, bool>>>()))
                .ReturnsAsync(existing);

            // Act: Click Laugh when Like exists (different groups)
            await _service.ToggleReaction(chatId, messageId, userId, MessageReactionType.Laugh);

            // Assert
            Assert.True(existing.Reaction.HasFlag(MessageReactionType.Like));
            Assert.True(existing.Reaction.HasFlag(MessageReactionType.Laugh));
            _reactionRepoMock.Verify(r => r.Update(existing), Times.Once);
        }

        [Fact]
        public async Task GetReactionCounts_ShouldReturnCorrectSums()
        {
            // Arrange
            long chatId = 1;
            long messageId = 100;
            var list = new List<MessageReaction>
            {
                new MessageReaction { ChatId = chatId, MessageId = messageId, UserId = 1, Reaction = MessageReactionType.Like | MessageReactionType.Laugh },
                new MessageReaction { ChatId = chatId, MessageId = messageId, UserId = 2, Reaction = MessageReactionType.Like | MessageReactionType.Think },
                new MessageReaction { ChatId = chatId, MessageId = messageId, UserId = 3, Reaction = MessageReactionType.Dislike }
            };

            _reactionRepoMock.Setup(r => r.GetAll()).Returns(list.AsQueryable());

            // Act
            var counts = await _service.GetReactionCounts(chatId, messageId);

            // Assert
            Assert.Equal(2, counts[MessageReactionType.Like]);
            Assert.Equal(1, counts[MessageReactionType.Dislike]);
            Assert.Equal(1, counts[MessageReactionType.Laugh]);
            Assert.Equal(0, counts[MessageReactionType.Sad]);
            Assert.Equal(1, counts[MessageReactionType.Think]);
            Assert.Equal(0, counts[MessageReactionType.Vomit]);
        }
    }
}
