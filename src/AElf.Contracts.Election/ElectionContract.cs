﻿using AElf.Common;
using AElf.Contracts.MultiToken.Messages;
using AElf.Kernel;
using Google.Protobuf.WellKnownTypes;
using Org.BouncyCastle.Asn1.Cms;

namespace AElf.Contracts.Election
{
    public class ElectionContract : ElectionContractContainer.ElectionContractBase
    {
        public override Empty InitialElectionContract(InitialElectionContractInput input)
        {
            Assert(!State.Initialized.Value, "Already initialized.");
            State.VoteContractSystemName.Value = input.VoteContractSystemName;
            State.TokenContractSystemName.Value = input.TokenContractSystemName;
            State.Initialized.Value = true;

            State.VoteContract.Register.Send(new VotingRegisterInput
            {
                Topic = ElectionContractConsts.Topic,
                Delegated = true,
                AcceptedCurrency = ElectionContractConsts.Symbol,
                ActiveDays = int.MaxValue,
                TotalEpoch = int.MaxValue
            });

            return new Empty();
        }

        /// <summary>
        /// Actually this method is for adding an option of voting.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Empty AnnounceElection(Empty input)
        {
            var publicKey = Context.RecoverPublicKey().ToHex();

            Assert(State.Votes[publicKey].ActiveVotes.Count == 0, "Voter can't announce election.");
            Assert(State.Candidates[publicKey] != true, "This public was either already announced or banned.");

            State.Candidates[publicKey] = true;

            // Add this alias to history information of this candidate.
            var candidateHistory = State.Histories[publicKey];
            if (candidateHistory != null)
            {
                candidateHistory.AnnouncementTransactionId = Context.TransactionId;
                State.Histories[publicKey] = candidateHistory;
            }
            else
            {
                State.Histories[publicKey] = new CandidateHistory
                {
                    AnnouncementTransactionId = Context.TransactionId
                };
            }

            State.TokenContract.Lock.Send(new LockInput
            {
                From = Context.Sender,
                To = Context.Self,
                Symbol = ElectionContractConsts.Symbol,
                Amount = ElectionContractConsts.LockTokenForElection,
                LockId = Context.TransactionId,
                Usage = "Lock for announcing election."
            });

            State.VoteContract.AddOption.Send(new AddOptionInput
            {
                Topic = ElectionContractConsts.Topic,
                Sponsor = Context.Self,
                Option = publicKey
            });

            return new Empty();
        }

        public override Empty QuitElection(Empty input)
        {
            var publicKey = Context.RecoverPublicKey().ToHex();

            State.Candidates[publicKey] = null;
            
            State.TokenContract.Unlock.Send(new UnlockInput
            {
                From = Context.Sender,
                To = Context.Self,
                Symbol = ElectionContractConsts.Symbol,
                LockId = State.Histories[publicKey].AnnouncementTransactionId,
                Amount = ElectionContractConsts.LockTokenForElection,
                Usage = "Quit election."
            });

            State.VoteContract.RemoveOption.Send(new RemoveOptionInput
            {
                Topic = ElectionContractConsts.Topic,
                Sponsor = Context.Self,
                Option = publicKey
            });

            return new Empty();
        }

        public override Empty Vote(VoteMinerInput input)
        {
            State.VoteContract.Vote.Send(new VoteInput
            {
                Topic = ElectionContractConsts.Topic,
                Sponsor = Context.Self,
                Amount = input.Amount,
                Option = input.CandidatePublicKey,
                Voter = Context.Sender,
                VoteId = Context.TransactionId
            });

            return new Empty();
        }

        public override Empty Withdraw(Hash input)
        {
            State.VoteContract.Withdraw.Send(new WithdrawInput
            {
                VoteId = input
            });

            return new Empty();
        }

        public override Empty UpdateTermNumber(UpdateTermNumberInput input)
        {
            State.VoteContract.UpdateEpochNumber.Send(new UpdateEpochNumberInput
            {
                EpochNumber = input.TermNumber,
                Topic = ElectionContractConsts.Topic
            });

            return new Empty();
        }

        public override ElectionResult GetElectionResult(GetElectionResultInput input)
        {
            var votingResult = State.VoteContract.GetVotingInfo.Call(new GetVotingResultInput
            {
                Topic = ElectionContractConsts.Topic,
                EpochNumber = input.TermNumber,
                Sponsor = Context.Self
            });

            var result = new ElectionResult
            {
                TermNumber = input.TermNumber,
                IsActive = input.TermNumber == State.CurrentTermNumber.Value,
                Results = {votingResult.Results}
            };

            return result;
        }
    }
}