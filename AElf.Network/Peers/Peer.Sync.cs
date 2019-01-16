﻿using System;
using System.Collections.Generic;
using System.Linq;
using AElf.ChainController.EventMessages;
using AElf.Common;
using AElf.Kernel;
using AElf.Network.Connection;
using AElf.Network.Data;
using Easy.MessageHub;
using Google.Protobuf;

namespace AElf.Network.Peers
{
    public partial class Peer
    {
        public const int DefaultRequestTimeout = 2000;

        public int RequestTimeout { get; set; } = DefaultRequestTimeout;
        public int MaxRequestRetries { get; set; } = 2;

        private const int GenesisHeight = 1;

        private readonly object _blockReqLock = new object();

        internal List<TimedBlockRequest> BlockRequests { get; }

        private readonly List<Announce> _announcements;

        /// <summary>
        /// When syncing history blocks this is the target height.
        /// </summary>
        public int SyncTarget { get; private set; }

        /// <summary>
        /// True if syncing to height.
        /// </summary>
        public bool IsSyncingHistory => SyncTarget != 0;

        /// <summary>
        /// When syncing an announcements, this is the current one.
        /// </summary>
        public Announce SyncedAnnouncement { get; private set; }

        /// <summary>
        /// True if syncing an announcement.
        /// </summary>
        public bool IsSyncingAnnounced => SyncedAnnouncement != null;

        /// <summary>
        /// Property that is true if we're currently syncing blocks from this peer.
        /// 
        /// </summary>
        public bool IsSyncing => IsSyncingHistory || IsSyncingAnnounced || IsDownloadingBranch;

        /// <summary>
        /// Represents our best knowledge about the peers height. This is updated
        /// based on the peers announcements.
        /// </summary>
        public int KnownHeight { get; private set; }

        /// <summary>
        /// When syncing history represents the currently requested block.
        /// </summary>
        public int CurrentlyRequestedHeight { get; private set; }

        /// <summary>
        /// Helper getter to probe for stashed announcements.
        /// </summary>
        public bool AnyStashed => _announcements.Any();

        public Announce GetLowestAnnouncement()
        {
            return _announcements?.OrderBy(a => a.Height).FirstOrDefault();
        }

        /// <summary>
        /// Resets all sync related state, as if this peer had just connected.
        /// </summary>
        public void ResetSync()
        {
            SyncTarget = 0;
            CurrentlyRequestedHeight = 0;
            SyncedAnnouncement = null;
            _announcements.Clear();
        }

        /// <summary>
        /// Effectively triggers a sync session with this peer. The target height is specified
        /// as a parameter.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="target"></param>
        public void SyncToHeight(int start, int target)
        {
            if (start <= GenesisHeight)
            {
                throw new InvalidOperationException("Cannot sync genesis height or lower.");
            }

            if (IsSyncing)
            {
                throw new InvalidOperationException("The peer is already syncing, " +
                                                    "this method should only be used to trigger an initial sync.");
            }

            // set sync state
            SyncTarget = target;
            CurrentlyRequestedHeight = start;
            
            _logger?.Debug($"[{this}] Syncing to height {SyncTarget}.");

            // request first
            RequestBlockByIndex(CurrentlyRequestedHeight);

            MessageHub.Instance.Publish(new ReceivingHistoryBlocksChanged(true));
        }

        /// <summary>
        /// This method will request the next block based on the current value of <see cref="CurrentlyRequestedHeight"/>.
        /// If target was reached, the state is reset and the method returns false.
        /// </summary>
        /// <returns>Returns whether or no this call has completed the sync.</returns>
        public bool SyncNextHistory()
        {
            if (CurrentlyRequestedHeight == SyncTarget)
            {
                // Clean any announcements that are lower than current height
                SyncTarget = 0;
                CurrentlyRequestedHeight = 0;
                MessageHub.Instance.Publish(new ReceivingHistoryBlocksChanged(false));
                return false;
            }

            CurrentlyRequestedHeight++;
            RequestBlockByIndex(CurrentlyRequestedHeight);

            return true;
        }

        public void StashAnnouncement(Announce announce)
        {
            if (announce?.Id == null)
                throw new ArgumentNullException($"{nameof(announce)} or its ID is null.");

            _announcements.Add(announce);
        }

        /// <summary>
        /// Removes announcements that have a height lower or equal than <param name="blockHeight"/>.
        /// </summary>
        /// <param name="blockHeight"></param>
        public void CleanAnnouncements(int blockHeight)
        {
            _announcements.RemoveAll(a => a.Height <= blockHeight);
        }

        public void CleanAnnouncement(byte[] blockId)
        {
            if (IsDisposed)
                return;

            var an = _announcements.FirstOrDefault(a => a.Id.ToByteArray().BytesEqual(blockId));

            if (an != null)
            {
                CleanAnnouncements(an.Height);
            }
        }

        /// <summary>
        /// Will trigger the request for the next announcement. If there's no more announcements to sync
        /// this method return false. The only way to trigger the sync is to call this method with an
        /// announcement previously added to the stash.
        /// </summary>
        /// <exception cref="InvalidOperationException">If this method is called when not syncing
        /// and with an empty cache the method throws.</exception>
        public byte[] SyncNextAnnouncement()
        {
            if (!IsSyncingAnnounced && !_announcements.Any())
                throw new InvalidOperationException($"Call to {nameof(SyncNextAnnouncement)} with no stashed announcements.");

            if (!_announcements.Any())
            {
                SyncedAnnouncement = null;
                return null;
            }

            var nextAnnouncement = _announcements.OrderBy(a => a.Height).First();

            SyncedAnnouncement = nextAnnouncement;
            _announcements.Remove(SyncedAnnouncement);

            var id = SyncedAnnouncement.Id.ToByteArray();
            RequestBlockById(id, SyncedAnnouncement.Height);

            return id;
        }

        public bool IsDownloadingBranch => LastRequested != null;
        public byte[] LastRequested { get; set; }

        public List<IBlock> _syncedBranch;

        // Download branch from authentification id
        public void StartDownloadBranch()
        {
            _syncedBranch = new List<IBlock>();
            LastRequested = _lastReceivedHandshake.HeadHash.ToByteArray();
                     
            RequestBlockById(LastRequested, _lastReceivedHandshake.Height);
        }

        public void AddBranchedBlock(IBlock block)
        {
            _syncedBranch.Add(block);
            LastRequested = block.Header.PreviousBlockHash.DumpByteArray();
            
            RequestBlockById(LastRequested, (int)block.Index-1);
        }

        public List<IBlock> FinishBranchDownload(IBlock block)
        {
            _syncedBranch.Add(block);

            var fullList = _syncedBranch.ToList();
            
            _syncedBranch = null;
            LastRequested = null;
            
            return fullList;
        }

        /// <summary>
        /// This method is used to update the height of the current peer.
        /// </summary>
        /// <param name="a"></param>
        public void OnAnnouncementMessage(Announce a)
        {
            if (a?.Id == null)
            {
                _logger?.Error($"[{this}] announcement or its id is null.");
                return;
            }

            try
            {
                if (a.Height <= KnownHeight)
                {
                    // todo just log for now, but this is probably a protocol error.
                    _logger?.Warn($"[{this}] current know height: {KnownHeight} announcement height {a.Height}.");
                }

                KnownHeight = a.Height;
            
                _logger?.Trace($"[{this}] received announcement, height increased: {KnownHeight}.");
            }
            catch (Exception e)
            {
                _logger?.Error(e, $"[{this}] error processing announcement.");
            }
        }

        /// <summary>
        /// This method is used to stop the timer for a block request.
        /// </summary>
        /// <param name="block"></param>
        public bool StopBlockTimer(Block block)
        {
            byte[] blockHash = block.GetHashBytes();
            int blockHeight = (int) block.Header.Index;

            lock (_blockReqLock)
            {
                TimedBlockRequest req = BlockRequests.FirstOrDefault(b => (b.IsById && b.Id.BytesEqual(blockHash)) || (!b.IsById && b.Height == blockHeight));

                if (req != null)
                {
                    req.Cancel();
                    BlockRequests.Remove(req);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public void RequestHeaders(int headerIndex, int headerRequestCount)
        {
            BlockHeaderRequest hReq = new BlockHeaderRequest {Height = headerIndex - 1, Count = headerRequestCount};
            Message message = NetRequestFactory.CreateMessage(AElfProtocolMsgType.HeaderRequest, hReq.ToByteArray());

            EnqueueOutgoing(message);
        }

        private void RequestBlockByIndex(int index)
        {
            // Create the request object
            BlockRequest br = new BlockRequest { Height = index };
            Message message = NetRequestFactory.CreateMessage(AElfProtocolMsgType.RequestBlock, br.ToByteArray());

            if (message.Payload == null)
            {
                _logger?.Warn($"[{this}] request for block at height {index} failed because payload is null.");
                return;
            }

            SendTimedRequest(message, br);
        }

        private void RequestBlockById(byte[] id, int height = 0)
        {
            // Create the request object
            BlockRequest br = new BlockRequest { Id = ByteString.CopyFrom(id), Height = height };
            Message message = NetRequestFactory.CreateMessage(AElfProtocolMsgType.RequestBlock, br.ToByteArray());

            if (message.Payload == null)
            {
                _logger?.Warn($"[{this}] request for block with id {id.ToHex()} failed because payload is null.");
                return;
            }

            SendTimedRequest(message, br);
        }

        private void SendTimedRequest(Message message, BlockRequest br)
        {
            TimedBlockRequest blockRequest = new TimedBlockRequest(message, br, RequestTimeout);
            blockRequest.SetCurrentPeer(this);
            blockRequest.RequestTimedOut += TimedRequestOnRequestTimedOut;

            lock (_blockReqLock)
            {
                BlockRequests.Add(blockRequest);
            }

            EnqueueOutgoing(message, (_) =>
            {
                blockRequest.Start();

                if (blockRequest.IsById)
                    _logger?.Trace($"[{this}] Block request sent {{ hash: {blockRequest.Id.ToHex()} }}");
                else
                    _logger?.Trace($"[{this}] Block request sent {{ height: {blockRequest.Height} }}");
            });
        }

        private void TimedRequestOnRequestTimedOut(object sender, EventArgs e)
        {
            if (sender is TimedBlockRequest req)
            {
                _logger?.Warn($"[{this}] failed timed request {req}");

                if (req.CurrentPeerRetries < MaxRequestRetries)
                {
                    if (!req.SetCurrentPeer(this))
                    {
                        return;
                    }

                    _logger?.Debug($"[{this}] try again {req}.");

                    EnqueueOutgoing(req.Message, (_) =>
                    {
                        // last check for cancellation
                        if (req.IsCanceled)
                            return;

                        req.Start();

                        if (req.IsById)
                            _logger?.Trace($"[{this}] Block request sent {{ hash: {req.Id.ToHex()} }}");
                        else
                            _logger?.Trace($"[{this}] Block request sent {{ height: {req.Height} }}");
                    });
                }
                else
                {
                    lock (_blockReqLock)
                    {
                        BlockRequests.RemoveAll(b => (b.IsById && b.Id.BytesEqual(req.Id)) || (!b.IsById && b.Height == req.Height));
                    }

                    _logger?.Warn($"[{this}] request failed {req}.");

                    req.RequestTimedOut -= TimedRequestOnRequestTimedOut;
                    
                    //Dispose();
                }
            }
        }
    }
}