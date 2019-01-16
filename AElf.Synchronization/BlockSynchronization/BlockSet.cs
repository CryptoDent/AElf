using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AElf.Common;
using AElf.Kernel;
using NLog;

namespace AElf.Synchronization.BlockSynchronization
{
    public class LibChangedArgs : EventArgs
    {
        public BlockState NewLib { get; set; }
    }
    
    public class BlockSet
    {
        public event EventHandler LibChanged;
            
        private const int Timeout = int.MaxValue;

        private readonly ILogger _logger;

        public BlockState CurrentHead { get; private set; }
        public BlockState CurrentLib { get; private set; }
        
        private List<BlockState> _blocks;

        private ReaderWriterLock _rwLock = new ReaderWriterLock();
        
        private List<string> _miners;

        public BlockSet()
        {
            _logger = LogManager.GetLogger(nameof(BlockSet));
        }
        
        public BlockState Init(List<string> miners, IBlock currentDbBlock)
        {
            if (miners.Count <= 0)
                throw new ArgumentException("Miners is empty");
            
            _miners = miners.ToList();
            _blocks = new List<BlockState>();
            
            CurrentHead = new BlockState(currentDbBlock, null, true, _miners);
            _blocks.Add(CurrentHead);
            
            if (currentDbBlock.Index == GlobalConfig.GenesisBlockHeight)
                CurrentLib = CurrentHead;
            
            return CurrentHead;
        }

        public void PushBlock(IBlock block)
        {
            _rwLock.AcquireReaderLock(Timeout);
            
            try
            {
                var newBlockHash = block.GetHash();
                if (_blocks.Any(b => newBlockHash == b.BlockHash))
                    return;
                
                var previous = _blocks.FirstOrDefault(pb => pb.BlockHash == block.Header.PreviousBlockHash);
                
                if (previous == null)
                    throw new UnlinkableBlockException();
    
                // change the head
                BlockState newState;
                if (previous == CurrentHead)
                {
                    // made current chain longer
                    newState = new BlockState(block, previous, true, _miners);
                    CurrentHead = newState;
                }
                else
                {
                    // made another chain longer
                    newState = new BlockState(block, previous, false, _miners);
                    
                    // if this other chain becomes higher than the head -> switch
                    if (newState.Index > CurrentHead.Index)
                    {
                        _logger?.Debug($"Switching chain ({CurrentHead.BlockHash} -> {newState.BlockHash})");
                        CurrentHead = newState;
                    }
                }

                var lc = _rwLock.UpgradeToWriterLock(Timeout);
                
                try
                {
                    _blocks.Add(newState);
                    
                    // update LIB
                    ulong libIndex = CurrentLib == null ? 0UL : CurrentLib.Index;
                    
//                    var blocksToConfirm = _blocks
//                        .Where(b => libIndex < b.Index && b.Index < CurrentHead.Index)
//                        .OrderByDescending(b => b.Index).ToList();

                    List<BlockState> blocksToConfirm = new List<BlockState>();
                    BlockState current = previous;
                    
                    while (current != null && current != CurrentLib && current.Index != 1)
                    {
                        blocksToConfirm.Add(current);
                        current = current.PreviousState;
                    }
                    
                    BlockState newLib = null;
                    foreach (var blk in blocksToConfirm)
                    {
                        var hasAll = blk.AddConfirmation(newState.Producer);
                        if (hasAll)
                        {
                            newLib = blk;
                            break;
                        }
                    }

                    if (newLib != null)
                    {
                        CurrentLib = newLib;
                        List<BlockState> blocksToRemove = _blocks.Where(b => b.Index < newLib.Index).ToList();

                        foreach (var blockState in blocksToRemove)
                        {
                            blockState.PreviousState = null;
                            _blocks.Remove(blockState);
                        }

                        CurrentLib.PreviousState = null;
                        
                        // todo clear branches
                        
                        FireLibChanged(newLib);
                    }
                }
                finally
                {
                    _rwLock.DowngradeFromWriterLock(ref lc);
                }
            }
            finally
            {
                _rwLock.ReleaseReaderLock();
            }
        }
        
        private void FireLibChanged(BlockState blockState)
        {
            EventHandler handler = LibChanged;
            if (handler != null)
            {
                handler(this, new LibChangedArgs { NewLib = blockState });
            }
        }

        public List<BlockState> GetBranch(BlockState branchTip, BlockState other)
        {
            List<BlockState> branchList = new List<BlockState>();
            List<BlockState> otherList = new List<BlockState>();

            BlockState currentBranchList = branchTip;
            BlockState currentOtherList = other;

            while (currentBranchList.Index > currentOtherList.Index)
            {
                branchList.Add(currentBranchList.GetCopyBlockState());
                currentBranchList = currentBranchList.PreviousState;
            }

            while (currentOtherList.Index > currentBranchList.Index)
            {
                otherList.Add(currentOtherList.GetCopyBlockState());
                currentOtherList = currentOtherList.PreviousState;
            }

            while (currentBranchList != currentOtherList)
            {
                if (currentBranchList.Previous == null || currentOtherList.Previous == null)
                    throw new InvalidOperationException("Invalid branch list.");
                
                branchList.Add(currentBranchList.GetCopyBlockState());
                otherList.Add(currentOtherList.GetCopyBlockState());
                
                currentBranchList = currentBranchList.PreviousState;
                currentOtherList = currentOtherList.PreviousState;
            }
            
            branchList.Add(currentBranchList.GetCopyBlockState());

            return branchList;
        }

        public bool IsBlockReceived(IBlock block)
        {
            _rwLock.AcquireReaderLock(Timeout);
            
            bool res;
            try
            {
                var blockHash = block.GetHash();
                res = _blocks.Any(b => blockHash == b.BlockHash);
            }
            finally
            {
                _rwLock.ReleaseReaderLock();
            }

            return res;
        }

        public BlockState GetBlockStateByHash(Hash blockHash)
        {
            _rwLock.AcquireReaderLock(Timeout);
            
            try
            {
                return _blocks.FirstOrDefault(b => b.BlockHash == blockHash);
            }
            finally
            {
                _rwLock.ReleaseReaderLock();
            }
        }

        public IBlock GetBlockByHash(Hash blockHash)
        {
            _rwLock.AcquireReaderLock(Timeout);
            
            BlockState blockSate;
            try
            {
                blockSate = _blocks.FirstOrDefault(b => b.BlockHash == blockHash);
            }
            finally
            {
                _rwLock.ReleaseReaderLock();
            }

            // todo review check blockbody (old behaviour)
            return blockSate?.BlockBody == null ? null : blockSate.GetClonedBlock();
        }

        // todo include IsCurrentBranch
        public IEnumerable<BlockState> GetBlocksByHeight(ulong height)
        {
            _rwLock.AcquireReaderLock(Timeout);
            
            try
            {
                return _blocks.Where(b => b.Index == height).ToList();
            }
            finally
            {
                _rwLock.ReleaseReaderLock();
            }
        }

        public void RemoveInvalidBlock(Hash blockHash)
        {
            if (blockHash == null)
                throw new ArgumentNullException(nameof(blockHash));
            
            _rwLock.AcquireWriterLock(Timeout);
            
            try
            {
                var toRemove = _blocks.FirstOrDefault(b => b.BlockHash == blockHash);

                if (toRemove == null)
                {
                    _logger?.Warn($"Cannot remove block {blockHash}, not found.");
                    return;
                }

                _blocks.Remove(toRemove);

                if (CurrentHead.BlockHash == blockHash)
                {
                    var prev = CurrentHead.PreviousState;
                    CurrentHead = prev;
                } 
                
                _logger?.Debug($"Removed {blockHash} from blockset. Head {CurrentHead.BlockHash}");
            }
            finally
            {
                _rwLock.ReleaseWriterLock();
            }
        }

        public void Clear()
        {
            _rwLock.AcquireWriterLock(Timeout);

            try
            {
                _blocks.RemoveAll(b => b != CurrentLib);
                CurrentHead = CurrentLib;
            }
            finally
            {
                _rwLock.ReleaseWriterLock();
            }
        }
    }
}