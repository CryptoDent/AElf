﻿using System.Linq;
using AElf.Common;
using AElf.Contracts.MultiToken.Messages;
using AElf.Kernel;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Vote
{
    public class VoteContract : VoteContractContainer.VoteContractBase
    {
        public override Empty InitialVoteContract(InitialVoteContractInput input)
        {
            Assert(!State.Initialized.Value, "Already initialized.");
            State.BasicContractZero.Value = Context.GetZeroSmartContractAddress();
            State.TokenContractSystemName.Value = input.TokenContractSystemName;

            State.Initialized.Value = true;

            return new Empty();
        }

        public override Empty Register(VotingRegisterInput input)
        {
            if (State.TokenContract.Value == null)
            {
                State.TokenContract.Value =
                    State.BasicContractZero.GetContractAddressByName.Call(State.TokenContractSystemName.Value);
            }

            var votingEvent = new VotingEvent
            {
                Sponsor = Context.Sender,
                Topic = input.Topic
            };
            var votingEventHash = votingEvent.GetHash();

            Assert(State.VotingEvents[votingEventHash] == null, "Voting event already exists.");
            Assert(input.TotalEpoch >= 1, "Invalid total epoch.");
            var tokenInfo = State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
            {
                Symbol = input.AcceptedCurrency
            });
            Assert(tokenInfo.LockWhiteList.Contains(Context.Self),
                "Claimed accepted token is not available for voting.");

            // Initialize VotingEvent.
            votingEvent.AcceptedCurrency = input.AcceptedCurrency;
            votingEvent.ActiveDays = input.ActiveDays;
            votingEvent.Delegated = input.Delegated;
            votingEvent.TotalEpoch = input.TotalEpoch;
            votingEvent.Options.AddRange(input.Options);
            votingEvent.CurrentEpoch = 1;
            State.VotingEvents[votingEventHash] = votingEvent;

            // Initialize VotingResult of Epoch 1.
            var votingResultHash = Hash.FromMessage(new GetVotingResultInput
            {
                Sponsor = Context.Sender,
                Topic = input.Topic,
                EpochNumber = 1
            });
            State.VotingResults[votingResultHash] = new VotingResult
            {
                Topic = input.Topic,
                Sponsor = Context.Sender
            };

            return new Empty();
        }

        public override Empty Vote(VoteInput input)
        {
            var votingEvent = AssertVotingEvent(input.Topic, input.Sponsor);

            // VoteId -> VotingRecord
            var votingRecord = new VotingRecord
            {
                Topic = input.Topic,
                Sponsor = input.Sponsor,
                Amount = input.Amount,
                EpochNumber = votingEvent.CurrentEpoch,
                Option = input.Option,
                IsWithdrawn = false,
                VoteTimestamp = Context.CurrentBlockTime.ToTimestamp(),
                Voter = votingEvent.Delegated ? input.Voter : Context.Sender,
                Currency = votingEvent.AcceptedCurrency
            };

            // Modify VotingResult
            var votingResultHash = Hash.FromMessage(new GetVotingResultInput
            {
                Sponsor = input.Sponsor,
                Topic = input.Topic,
                EpochNumber = votingEvent.CurrentEpoch
            });
            var votingResult = State.VotingResults[votingResultHash];
            var currentVotes = votingResult.Results[input.Option];
            votingResult.Results[input.Option] = currentVotes + input.Amount;

            // Update voting history
            var votingHistories = State.VotingHistoriesMap[votingRecord.Voter] ?? new VotingHistories
            {
                Voter = votingRecord.Voter
            };
            var eventVotingHistories = votingHistories.Votes[votingEvent.GetHash().ToHex()];
            if (eventVotingHistories == null)
            {
                votingHistories.Votes[votingEvent.GetHash().ToHex()] = new VotingHistory
                {
                    ActiveVotes = {input.VoteId}
                };
                votingResult.VotersCount += 1;
            }
            else
            {
                votingHistories.Votes[votingEvent.GetHash().ToHex()].ActiveVotes.Add(input.VoteId);
            }

            State.VotingRecords[input.VoteId] = votingRecord;

            State.VotingResults[votingResultHash] = votingResult;

            State.VotingHistoriesMap[votingRecord.Voter] = votingHistories;

            // Lock voted token.
            State.TokenContract.Lock.Send(new LockInput
            {
                From = votingRecord.Voter,
                Symbol = votingEvent.AcceptedCurrency,
                LockId = input.VoteId,
                Amount = input.Amount,
                To = input.Sponsor,
                Usage = $"Voting for {input.Topic}"
            });

            return new Empty();
        }

        public override Empty Withdraw(WithdrawInput input)
        {
            var votingRecord = State.VotingRecords[input.VoteId];
            Assert(votingRecord != null, "Voting record not found.");
            if (votingRecord == null)
            {
                return new Empty();
            }

            votingRecord.IsWithdrawn = true;
            votingRecord.WithdrawTimestamp = Context.CurrentBlockTime.ToTimestamp();
            State.VotingRecords[input.VoteId] = votingRecord;

            var votingEventHash =
                new VotingEvent {Topic = votingRecord.Topic, Sponsor = votingRecord.Sponsor}.GetHash();

            var votingHistory = UpdateHistoryAfterWithdrawing(votingRecord.Voter, votingEventHash, input.VoteId);
            
            if (!votingHistory.Votes[votingEventHash.ToHex()].ActiveVotes.Any())
            {
                var votingResultHash = Hash.FromMessage(new GetVotingResultInput
                {
                    Sponsor = votingRecord.Sponsor,
                    Topic = votingRecord.Topic,
                    EpochNumber = votingRecord.EpochNumber
                });
                var votingResult = State.VotingResults[votingResultHash];
                votingResult.VotersCount -= 1;
                State.VotingResults[votingResultHash] = votingResult;
            }
            
            State.TokenContract.Unlock.Send(new UnlockInput
            {
                From = votingRecord.Voter,
                Symbol = votingRecord.Currency,
                Amount = votingRecord.Amount,
                LockId = input.VoteId,
                To = votingRecord.Sponsor,
                Usage = $"Withdraw votes for {votingRecord.Topic}"
            });
            return new Empty();
        }

        public override Empty UpdateEpochNumber(UpdateEpochNumberInput input)
        {
            var votingEvent = AssertVotingEvent(input.Topic, Context.Sender);
            votingEvent.CurrentEpoch = input.EpochNumber;
            State.VotingEvents[votingEvent.GetHash()] = votingEvent;
            return new Empty();
        }

        public override VotingResult GetVotingInfo(GetVotingResultInput input)
        {
            var votingResultHash = Hash.FromMessage(input);
            return State.VotingResults[votingResultHash];
        }

        public override Empty AddOption(AddOptionInput input)
        {
            var votingEvent = AssertVotingEvent(input.Topic, input.Sponsor);
            Assert(votingEvent.Sponsor == Context.Sender, "Only sponsor can update options.");
            Assert(!votingEvent.Options.Contains(input.Option), "Option already exists.");
            votingEvent.Options.Add(input.Option);
            State.VotingEvents[votingEvent.GetHash()] = votingEvent;
            return new Empty();
        }

        public override Empty RemoveOption(RemoveOptionInput input)
        {
            var votingEvent = AssertVotingEvent(input.Topic, input.Sponsor);
            Assert(votingEvent.Sponsor == Context.Sender, "Only sponsor can update options.");
            Assert(votingEvent.Options.Contains(input.Option), "Option doesn't exist.");
            votingEvent.Options.Remove(input.Option);
            State.VotingEvents[votingEvent.GetHash()] = votingEvent;
            return new Empty();
        }

        public override VotingHistories GetVotingHistories(Address input)
        {
            return State.VotingHistoriesMap[input];
        }

        public override VotingHistory GetVotingHistory(GetVotingHistoryInput input)
        {
            var votingEvent = AssertVotingEvent(input.Topic, input.Sponsor);
            var votes = State.VotingHistoriesMap[input.Voter].Votes[votingEvent.GetHash().ToHex()];
            var activeVotes = votes.ActiveVotes;
            var withdrawnVotes = votes.WithdrawnVotes;
            return new VotingHistory
            {
                ActiveVotes = {activeVotes}, WithdrawnVotes = {withdrawnVotes}
            };
        }

        private VotingEvent AssertVotingEvent(string topic, Address sponsor)
        {
            var votingEvent = new VotingEvent
            {
                Topic = topic,
                Sponsor = sponsor
            };
            var votingEventHash = votingEvent.GetHash();
            Assert(State.VotingEvents[votingEventHash] != null, "Voting event not found.");
            return State.VotingEvents[votingEventHash];
        }

        private VotingHistories UpdateHistoryAfterWithdrawing(Address voter, Hash votingEventHash, Hash voteId)
        {
            var votingHistory = State.VotingHistoriesMap[voter];
            votingHistory.Votes[votingEventHash.ToHex()].ActiveVotes.Remove(voteId);
            votingHistory.Votes[votingEventHash.ToHex()].WithdrawnVotes.Add(voteId);
            State.VotingHistoriesMap[voter] = votingHistory;
            return votingHistory;
        }
    }
}