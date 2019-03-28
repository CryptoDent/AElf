using System;
using System.Linq;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Consensus.DPoS
{
    /// <summary>
    /// Extension methods of Round for dealing with miners' mining time.
    /// </summary>
    public static class MiningTimeExtensions
    {
        /// <summary>
        /// Simply read the expected mining time of provided public key from round information.
        /// Do not check this node missed his time slot or not.
        /// </summary>
        /// <param name="round"></param>
        /// <param name="publicKey"></param>
        /// <returns>If provided public key not contained in current miners list, will return an invalid mining time.</returns>
        public static Timestamp GetExpectedMiningTime(this Round round, string publicKey)
        {
            return round.RealTimeMinersInformation.ContainsKey(publicKey)
                ? round.RealTimeMinersInformation[publicKey].ExpectedMiningTime
                : DateTime.MaxValue.ToUniversalTime().ToTimestamp();
        }

        /// <summary>
        /// If one node produced block this round or missed his time slot,
        /// whatever how long he missed, we can give him a consensus command with new time slot
        /// to produce a block (for terminating current round and start new round).
        /// The schedule generated by this command will be cancelled
        /// if this node executed blocks from other nodes.
        /// 
        /// Notice:
        /// This method shouldn't return the expected mining time from round information.
        /// To prevent this kind of misuse, this method will return a invalid timestamp
        /// when this node hasn't missed his time slot.
        /// </summary>
        /// <returns></returns>
        public static Timestamp ArrangeAbnormalMiningTime(this Round round, string publicKey, DateTime dateTime,
            int miningInterval = 0)
        {
            if (!round.RealTimeMinersInformation.ContainsKey(publicKey))
            {
                return DateTime.MaxValue.ToUniversalTime().ToTimestamp();
            }
            
            if (miningInterval == 0)
            {
                miningInterval = round.GetMiningInterval();
            }
            
            if (round.RoundNumber == 1)
            {
                var offset = miningInterval * round.RealTimeMinersInformation[publicKey].Order;
                return dateTime.AddMilliseconds(round.TotalMilliseconds() + offset).ToTimestamp();
            }

            if (!round.IsTimeSlotPassed(publicKey, dateTime, out var minerInRound) && minerInRound.OutValue == null)
            {
                return DateTime.MaxValue.ToUniversalTime().ToTimestamp();
            }

            if (round.GetExtraBlockProducerInformation().PublicKey == publicKey)
            {
                var distance = (round.GetExtraBlockMiningTime() - dateTime).TotalMilliseconds;
                if (distance > 0)
                {
                    return round.GetExtraBlockMiningTime().ToTimestamp();
                }
            }

            if (round.RealTimeMinersInformation.ContainsKey(publicKey) && miningInterval > 0)
            {
                var distanceToRoundStartTime =
                    (dateTime - round.GetStartTime()).TotalMilliseconds;
                var missedRoundsCount = (int) (distanceToRoundStartTime / round.TotalMilliseconds(miningInterval));
                var expectedEndTime = round.GetExpectedEndTime(missedRoundsCount, miningInterval);
                return expectedEndTime.ToDateTime().AddMilliseconds(minerInRound.Order * miningInterval).ToTimestamp();
            }

            // Never do the mining if this node has no privilege to mime or the mining interval is invalid.
            return DateTime.MaxValue.ToUniversalTime().ToTimestamp();
        }

        /// <summary>
        /// This method is only available when the miners of this round is more than 1.
        /// </summary>
        /// <param name="round"></param>
        /// <returns></returns>
        public static int GetMiningInterval(this Round round)
        {
            if (round.RealTimeMinersInformation.Count == 1)
            {
                // Just appoint the mining interval for single miner.
                return 4000;
            }

            var firstTwoMiners = round.RealTimeMinersInformation.Values.Where(m => m.Order == 1 || m.Order == 2)
                .ToList();
            var distance =
                (int) (firstTwoMiners[1].ExpectedMiningTime.ToDateTime() -
                       firstTwoMiners[0].ExpectedMiningTime.ToDateTime())
                .TotalMilliseconds;
            return distance > 0 ? distance : -distance;
        }
        
        /// <summary>
        /// In current DPoS design, each miner produce his block in one time slot, then the extra block producer
        /// produce a block to terminate current round and confirm the mining order of next round.
        /// So totally, the time of one round is:
        /// MiningInterval * MinersCount + MiningInterval.
        /// </summary>
        /// <param name="round"></param>
        /// <param name="miningInterval"></param>
        /// <returns></returns>                                                
        public static int TotalMilliseconds(this Round round, int miningInterval = 0)
        {
            if (miningInterval == 0)
            {
                miningInterval = round.GetMiningInterval();
            }

            return round.RealTimeMinersInformation.Count * miningInterval + miningInterval;
        }

        /// <summary>
        /// Actually the expected mining time of the miner whose order is 1.
        /// </summary>
        /// <param name="round"></param>
        /// <returns></returns>
        public static DateTime GetStartTime(this Round round)
        {
            return round.RealTimeMinersInformation.Values.First(m => m.Order == 1).ExpectedMiningTime.ToDateTime();
        }

        /// <summary>
        /// This method for now is able to handle the situation of a miner keeping offline so many rounds,
        /// by using missedRoundsCount.
        /// </summary>
        /// <param name="round"></param>
        /// <param name="miningInterval"></param>
        /// <param name="missedRoundsCount"></param>
        /// <returns></returns>
        public static Timestamp GetExpectedEndTime(this Round round, int missedRoundsCount = 0, int miningInterval = 0)
        {
            if (miningInterval == 0)
            {
                miningInterval = round.GetMiningInterval();
            }

            return round.GetStartTime().AddMilliseconds(round.TotalMilliseconds(miningInterval))
                // Arrange an ending time if this node missed so many rounds.
                .AddMilliseconds(missedRoundsCount * round.TotalMilliseconds(miningInterval))
                .ToTimestamp();
        }
        
        /// <summary>
        /// For now, if current time is behind the end of expected mining time slot,
        /// we can say this node missed his time slot.
        /// </summary>
        /// <param name="round"></param>
        /// <param name="publicKey"></param>
        /// <param name="dateTime"></param>
        /// <param name="minerInRound"></param>
        /// <returns></returns>
        public static bool IsTimeSlotPassed(this Round round, string publicKey, DateTime dateTime,
            out MinerInRound minerInRound)
        {
            minerInRound = null;
            var miningInterval = round.GetMiningInterval();
            if (round.RealTimeMinersInformation.ContainsKey(publicKey))
            {
                minerInRound = round.RealTimeMinersInformation[publicKey];
                return minerInRound.ExpectedMiningTime.ToDateTime().AddMilliseconds((double) miningInterval / 2) <
                       dateTime;
            }

            return false;
        }
        
        public static DateTime GetExtraBlockMiningTime(this Round round)
        {
            return round.RealTimeMinersInformation.OrderBy(m => m.Value.ExpectedMiningTime.ToDateTime()).Last().Value
                .ExpectedMiningTime.ToDateTime()
                .AddMilliseconds(round.GetMiningInterval());
        }
        
        public static Timestamp GetArrangedTimestamp(this Timestamp timestamp, int order, int miningInterval)
        {
            return timestamp.ToDateTime().AddMilliseconds(miningInterval * order).ToTimestamp();
        }
        
        public static bool IsTimeToChangeTerm(this Round round, Round previousRound, DateTime blockchainStartTime,
            long termNumber)
        {
            var minersCount = previousRound.RealTimeMinersInformation.Values.Count(m => m.OutValue != null);
            var minimumCount = ((int) ((minersCount * 2d) / 3)) + 1;
            var approvalsCount = round.RealTimeMinersInformation.Values.Where(m => m.ActualMiningTime != null)
                .Select(m => m.ActualMiningTime)
                .Count(t => IsTimeToChangeTerm(blockchainStartTime, t.ToDateTime(), termNumber));
            return approvalsCount >= minimumCount;
        }

        /// <summary>
        /// If DaysEachTerm == 7:
        /// 1, 1, 1 => 0 != 1 - 1 => false
        /// 1, 2, 1 => 0 != 1 - 1 => false
        /// 1, 8, 1 => 1 != 1 - 1 => true => term number will be 2
        /// 1, 9, 2 => 1 != 2 - 1 => false
        /// 1, 15, 2 => 2 != 2 - 1 => true => term number will be 3.
        /// </summary>
        /// <param name="blockchainStartTimestamp"></param>
        /// <param name="termNumber"></param>
        /// <param name="blockProducedTimestamp"></param>
        /// <returns></returns>
        private static bool IsTimeToChangeTerm(DateTime blockchainStartTimestamp, DateTime blockProducedTimestamp,
            long termNumber)
        {
            return (long) (blockProducedTimestamp - blockchainStartTimestamp).TotalMinutes /
                   ConsensusDPoSConsts.DaysEachTerm != termNumber - 1;
        }
    }
}