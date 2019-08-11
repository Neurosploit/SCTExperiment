namespace Moloch
{
    using Stratis.SmartContracts;
    using Stratis.SmartContracts.Standards;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;

    /// <summary>
    ///  Initial attempt for the implementation of the molach smart contract for the stratis platform.
    /// </summary>
    public class Moloch : SmartContract
    {
        // default = 17280 = 4.8 hours in seconds (5 periods per day)
        public ulong PeriodDuration
        {
            get
            {
                return this.PersistentState.GetUInt64("PeriodDuration");
            }

            private set
            {
                this.PersistentState.SetUInt64("PeriodDuration", value);
            }
        }

        // default = 35 periods (7 days)
        public ulong VotingPeriodLength
        {
            get
            {
                return this.PersistentState.GetUInt64("VotingPeriodLength");
            }

            private set
            {
                this.PersistentState.SetUInt64("VotingPeriodLength", value);
            }
        }

        // default = 35 periods (7 days)
        public ulong GracePeriodLength
        {
            get
            {
                return this.PersistentState.GetUInt64("GracePeriodLength");
            }

            private set
            {
                this.PersistentState.SetUInt64("GracePeriodLength", value);
            }
        }

        // default = 5 periods (1 day)
        public ulong AbortWindow
        {
            get
            {
                return this.PersistentState.GetUInt64("AbortWindow");
            }

            private set
            {
                this.PersistentState.SetUInt64("AbortWindow", value);
            }
        }

        // default = 10 ETH (~$1,000 worth of ETH at contract deployment)
        public ulong ProposalDeposit
        {
            get
            {
                return this.PersistentState.GetUInt64("ProposalDeposit");
            }

            private set
            {
                this.PersistentState.SetUInt64("ProposalDeposit", value);
            }
        }

        // default = 3 - maximum multiplier a YES voter will be obligated to pay in case of mass ragequit
        public ulong DilutionBound
        {
            get
            {
                return this.PersistentState.GetUInt64("DilutionBound");
            }

            private set
            {
                this.PersistentState.SetUInt64("DilutionBound", value);
            }
        }

        // default = 0.1 - amount of ETH to give to whoever processes a proposal
        public ulong ProcessingReward
        {
            get
            {
                return this.PersistentState.GetUInt64("ProcessingReward");
            }

            private set
            {
                this.PersistentState.SetUInt64("ProcessingReward", value);
            }
        }

        public long SummoningTime
        {
            get
            {
                return this.PersistentState.GetInt64("SummoningTime");
            }

            private set
            {
                this.PersistentState.SetInt64("SummoningTime", value);
            }
        }

        public Address ApprovedTokenAddress
        {
            get
            {
                return this.PersistentState.GetAddress("ApprovedTokenAddress");
            }
            private set
            {
                this.PersistentState.SetAddress("ApprovedTokenAddress", value);
            }
        }

        // todo: how to wrap calls to IStandardtoken in an easy way? Do I need to write a wrapper that uses .Call? I would like to have something like IContractReference<IStandardToken> that does all that for me but can it be done?
        public IStandardToken ApprovedStandardToken { get; set; }

        // todo: can we create a contract from another and use it's reference immediately? 
        public GuildBank GuildBank { get; set; }

        // HARD-CODED LIMITS
        // These numbers are quite arbitrary; they are small enough to avoid overflows when doing calculations
        // with periods or shares, yet big enough to not limit reasonable use cases.
        // todo: 10**18
        const ulong MAX_VOTING_PERIOD_LENGTH = 1000000000000000000; // maximum length of voting period
        const ulong MAX_GRACE_PERIOD_LENGTH = 1000000000000000000; // maximum length of grace period
        const ulong MAX_DILUTION_BOUND = 1000000000000000000; // maximum dilution bound
        const ulong MAX_NUMBER_OF_SHARES = 1000000000000000000; // maximum number of shares that can be minted

        public ulong TotalShares
        {
            get
            {
                return this.PersistentState.GetUInt64("TotalShares");
            }

            private set
            {
                this.PersistentState.SetUInt64("TotalShares", value);
            }
        } // total shares across all members

        public ulong TotalSharesRequested
        {
            get
            {
                return this.PersistentState.GetUInt64("TotalSharesRequested");
            }

            private set
            {
                this.PersistentState.SetUInt64("TotalSharesRequested", value);
            }
        } // total shares that have been requested in unprocessed proposals

        public enum Vote
        {
            Null = 0, // default value, counted as abstention
            Yes = 1,
            No = 2
        }

        public class Member
        {
            public Address DelegateKey { get; set; } // the key responsible for submitting proposals and voting - defaults to member address unless updated

            public ulong Shares { get; set; } // the # of shares assigned to this member

            public bool Exists { get; set; } // always true once a member has been created

            public ulong HighestIndexYesVote { get; set; } // highest proposal index # on which the member voted YES
        }

        public class Proposal
        {
            public Proposal()
            {
                VotesByMember = new Dictionary<Address, Vote>();
            }

            public Address Proposer { get; set; }  // the member who submitted the proposal

            public Address Applicant { get; set; }  // the applicant who wishes to become a member - this key will be used for withdrawals

            public ulong SharesRequested { get; set; }  // the # of shares the applicant is requesting

            public ulong StartingPeriod { get; set; }  // the period in which voting can start for this proposal

            public ulong YesVotes { get; set; }  // the total number of YES votes for this proposal

            public ulong NoVotes { get; set; }  // the total number of NO votes for this proposal

            public bool Processed { get; set; }  // true only if the proposal has been processed

            public bool DidPass { get; set; }  // true only if the proposal passed

            public bool Aborted { get; set; }  // true only if applicant calls "abort" fn before end of voting period

            public ulong TokenTribute { get; set; }  // amount of tokens offered as tribute

            public string Details { get; set; }  // proposal details - could be IPFS hash, plaintext, or JSON

            public ulong MaxTotalSharesAtYesVote { get; set; }  // the maximum # of total shares encountered at a yes vote on this proposal

            public Dictionary<Address, Vote> VotesByMember { get; private set; } // the votes on this proposal by each member
        }

        public Address GetMemberAddressByDelegateKey(Address address)
        {
            return PersistentState.GetAddress($"MemberAddressByDelegateKey:{address}");
        }

        private void SetMemberAddressByDelegateKey(Address address, Address value)
        {
            PersistentState.SetAddress($"MemberAddressByDelegateKey:{address}", value);
        }

        public Member GetMember(Address address)
        {
            var bytes = PersistentState.GetBytes($"Members:{address}");
            return Deserialize<Member>(bytes);
        }

        private void SetMember(Address address, Member member)
        {
            var bytes = Serialize(member);
            PersistentState.SetBytes($"Members:{address}", bytes);
        }

        public Proposal[] GetProposalQueue()
        {
            return PersistentState.GetArray<Proposal>("ProposalQueue");
        }

        public void SetProposalQueue(Proposal[] proposalQueue)
        {
            PersistentState.SetArray("ProposalQueue", proposalQueue);
        }

        public void AssertOnlyMember()
        {
            Assert(IsOnlyMember(), "Moloch::onlyMember - not a member");
        }

        public bool IsOnlyMember()
        {
            return GetMember(Message.Sender).Shares > 0;
        }

        public void AssertOnlyDelegate()
        {
            Assert(IsOnlyDelegate(), "Moloch::onlyDelegate - not a delegate");
        }

        public bool IsOnlyDelegate()
        {
            return GetMember(GetMemberAddressByDelegateKey(Message.Sender)).Shares > 0;
        }

        private void LogSubmitProposal(ulong proposalIndex, Address delegateKey, Address memberAddress, Address applicant, ulong tokenTribute, ulong sharesRequested)
        {
            Log(new SubmitProposalLog()
            {
                ProposalIndex = proposalIndex,
                DelegateKey = delegateKey,
                MemberAddress = memberAddress,
                Applicant = applicant,
                TokenTribute = tokenTribute,
                SharesRequested = sharesRequested
            });
        }

        public struct SubmitProposalLog
        {
            public ulong ProposalIndex;

            [Index]
            public Address DelegateKey;

            [Index]
            public Address MemberAddress;

            [Index]
            public Address Applicant;

            public ulong TokenTribute;

            public ulong SharesRequested;
        }

        private void LogSubmitVote(ulong proposalIndex, Address delegateKey, Address memberAddress, byte vote)
        {
            Log(new SubmitVoteLog()
            {
                ProposalIndex = proposalIndex,
                DelegateKey = delegateKey,
                MemberAddress = memberAddress,
                Vote = vote
            });
        }

        public struct SubmitVoteLog
        {
            [Index]
            public ulong ProposalIndex;

            [Index]
            public Address DelegateKey;

            [Index]
            public Address MemberAddress;

            public byte Vote;
        }

        private void LogProcessProposal(ulong proposalIndex, Address applicant, Address memberAddress, ulong tokenTribute, ulong sharesRequested, bool didPass)
        {
            Log(new ProcessProposalLog
            {
                ProposalIndex = proposalIndex,
                Applicant = applicant,
                MemberAddress = memberAddress,
                TokenTribute = tokenTribute,
                SharesRequested = sharesRequested,
                DidPass = didPass
            });
        }

        public struct ProcessProposalLog
        {
            [Index]
            public ulong ProposalIndex;

            [Index]
            public Address Applicant;

            [Index]
            public Address MemberAddress;

            public ulong TokenTribute;

            public ulong SharesRequested;

            public bool DidPass;
        }

        private void LogRagequit(Address memberAddress, ulong sharesToBurn)
        {
            Log(new RagequitLog() { MemberAddress = memberAddress, SharesToBurn = sharesToBurn });
        }

        public struct RagequitLog
        {
            [Index]
            public Address MemberAddress;

            public ulong SharesToBurn;
        }

        private void LogAbort(ulong proposalIndex, Address applicantAddress)
        {
            Log(new AbortLog { ProposalIndex = proposalIndex, ApplicantAddress = applicantAddress });
        }

        public struct AbortLog
        {
            [Index]
            public ulong ProposalIndex;

            public Address ApplicantAddress;
        }

        private void LogUpdateDelegateKey(Address memberAddress, Address newDelegateKey)
        {
            Log(new UpdateDelegateKeyLog() { MemberAddress = memberAddress, NewDelegateKey = newDelegateKey });
        }

        public struct UpdateDelegateKeyLog
        {
            [Index]
            public Address MemberAddress;

            public Address NewDelegateKey;
        }

        private void LogSummonComplete(Address summoner, ulong shares)
        {
            Log(new SummonCompleteLog() { Summoner = summoner, Shares = shares });
        }

        public struct SummonCompleteLog
        {
            [Index]
            public Address Summoner;

            public ulong Shares;
        }

        public Moloch(
            ISmartContractState state,
            Address summoner,
            Address _approvedToken,
            ulong _periodDuration,
            ulong _votingPeriodLength,
            ulong _gracePeriodLength,
            ulong _abortWindow,
            ulong _proposalDeposit,
            ulong _dilutionBound,
            ulong _processingReward,
            Address approvedTokenAddress,
            IStandardToken approvedStandardToken
            ) : base(state)
        {
            Assert(summoner != Address.Zero, "Moloch::constructor - summoner cannot be 0");
            Assert(_approvedToken != Address.Zero, "Moloch::constructor - _approvedToken cannot be 0");
            Assert(_periodDuration > 0, "Moloch::constructor - _periodDuration cannot be 0");
            Assert(_votingPeriodLength > 0, "Moloch::constructor - _votingPeriodLength cannot be 0");
            Assert(_votingPeriodLength <= MAX_VOTING_PERIOD_LENGTH, "Moloch::constructor - _votingPeriodLength exceeds limit");
            Assert(_gracePeriodLength <= MAX_GRACE_PERIOD_LENGTH, "Moloch::constructor - _gracePeriodLength exceeds limit");
            Assert(_abortWindow > 0, "Moloch::constructor - _abortWindow cannot be 0");
            Assert(_abortWindow <= _votingPeriodLength, "Moloch::constructor - _abortWindow must be smaller than or equal to _votingPeriodLength");
            Assert(_dilutionBound > 0, "Moloch::constructor - _dilutionBound cannot be 0");
            Assert(_dilutionBound <= MAX_DILUTION_BOUND, "Moloch::constructor - _dilutionBound exceeds limit");
            Assert(_proposalDeposit >= _processingReward, "Moloch::constructor - _proposalDeposit cannot be smaller than _processingReward");

            // todo: how to wrap calls from/to?
            ApprovedTokenAddress = approvedTokenAddress;
            ApprovedStandardToken = approvedStandardToken;

            // todo: how to start a new guildbank contract as well?
            GuildBank = new GuildBank(state, approvedTokenAddress, approvedStandardToken);

            PeriodDuration = _periodDuration;
            VotingPeriodLength = _votingPeriodLength;
            GracePeriodLength = _gracePeriodLength;
            AbortWindow = _abortWindow;
            ProposalDeposit = _proposalDeposit;
            DilutionBound = _dilutionBound;
            ProcessingReward = _processingReward;

            // todo: summoningTime = now; // now operator/call missing in stratis?
            SummoningTime = DateTime.UtcNow.Ticks;

            SetMember(summoner, new Member()
            {
                DelegateKey = summoner,
                Shares = 1,
                Exists = true,
                HighestIndexYesVote = 0
            });

            SetMemberAddressByDelegateKey(summoner, summoner);

            TotalShares = 1;
            SetProposalQueue(new Proposal[0]);

            LogSummonComplete(summoner, 1);
        }

        public void SubmitProposal(
           Address applicant,
           ulong tokenTribute,
           ulong sharesRequested,
           string details)
        {
            AssertOnlyDelegate();

            Assert(applicant != Address.Zero, "Moloch::submitProposal - applicant cannot be 0");

            // Make sure we won't run into overflows when doing calculations with shares.
            // Note that totalShares + totalSharesRequested + sharesRequested is an upper bound
            // on the number of shares that can exist until this proposal has been processed.
            Assert(TotalShares.Add(TotalSharesRequested).Add(sharesRequested) <= MAX_NUMBER_OF_SHARES, "Moloch::submitProposal - too many shares requested");

            TotalSharesRequested = TotalSharesRequested.Add(sharesRequested);

            Address memberAddress = GetMemberAddressByDelegateKey(Message.Sender);

            // todo: correct equivalent of address(this)?  
            // collect proposal deposit from proposer and store it in the Moloch until the proposal is processed
            Assert(ApprovedStandardToken.TransferFrom(Message.Sender, this.Address, ProposalDeposit), "Moloch::submitProposal - proposal deposit token transfer failed");

            // todo: correct equivalent of address(this)?  
            // collect tribute from applicant and store it in the Moloch until the proposal is processed
            Assert(ApprovedStandardToken.TransferFrom(applicant, this.Address, tokenTribute), "Moloch::submitProposal - tribute token transfer failed");

            // compute startingPeriod for proposal
            ulong startingPeriod = Max(
                GetCurrentPeriod(),
                GetProposalQueueLength() == 0 ? 0 : GetProposalQueue()[GetProposalQueueLength().Sub(1)].StartingPeriod
            ).Add(1);

            // create proposal ...
            Proposal proposal = new Proposal()
            {
                Proposer = memberAddress,
                Applicant = applicant,
                SharesRequested = sharesRequested,
                StartingPeriod = startingPeriod,
                YesVotes = 0,
                NoVotes = 0,
                Processed = false,
                DidPass = false,
                Aborted = false,
                TokenTribute = tokenTribute,
                Details = details,
                MaxTotalSharesAtYesVote = 0
            };

            // ... and append it to the queue
            var proposalQueue = GetProposalQueue();

            Array.Resize(ref proposalQueue, proposalQueue.Length + 1);
            proposalQueue[proposalQueue.GetUpperBound(0)] = proposal;

            SetProposalQueue(proposalQueue);

            ulong proposalIndex = ((ulong)proposalQueue.Length).Sub(1);
            LogSubmitProposal(proposalIndex, Message.Sender, memberAddress, applicant, tokenTribute, sharesRequested);
        }


        public void SubmitVote(ulong proposalIndex, byte uintVote)
        {
            AssertOnlyDelegate();

            Address memberAddress = GetMemberAddressByDelegateKey(Message.Sender);
            Member member = GetMember(memberAddress);

            var proposalQueue = GetProposalQueue();

            Assert(proposalIndex < (ulong)proposalQueue.Length, "Moloch::submitVote - proposal does not exist");
            Proposal proposal = proposalQueue[proposalIndex];

            Assert(uintVote < 3, "Moloch::submitVote - uintVote must be less than 3");
            Vote vote = Enum.Parse<Vote>(uintVote.ToString());

            Assert(GetCurrentPeriod() >= proposal.StartingPeriod, "Moloch::submitVote - voting period has not started");
            Assert(!HasVotingPeriodExpired(proposal.StartingPeriod), "Moloch::submitVote - proposal voting period has expired");
            Assert(proposal.VotesByMember[memberAddress] == Vote.Null, "Moloch::submitVote - member has already voted on this proposal");
            Assert(vote == Vote.Yes || vote == Vote.No, "Moloch::submitVote - vote must be either Yes or No");
            Assert(!proposal.Aborted, "Moloch::submitVote - proposal has been aborted");

            // store vote
            if (proposal.VotesByMember.ContainsKey(memberAddress))
            {
                proposal.VotesByMember[memberAddress] = vote;
            }
            else
            {
                proposal.VotesByMember.Add(memberAddress, vote);
            }

            // count vote
            if (vote == Vote.Yes)
            {
                proposal.YesVotes = proposal.YesVotes.Add(member.Shares);

                // set highest index (latest) yes vote - must be processed for member to ragequit
                if (proposalIndex > member.HighestIndexYesVote)
                {
                    member.HighestIndexYesVote = proposalIndex;
                }

                // set maximum of total shares encountered at a yes vote - used to bound dilution for yes voters
                if (TotalShares > proposal.MaxTotalSharesAtYesVote)
                {
                    proposal.MaxTotalSharesAtYesVote = TotalShares;
                }

            }
            else if (vote == Vote.No)
            {
                proposal.NoVotes = proposal.NoVotes.Add(member.Shares);
            }

            proposalQueue[proposalIndex] = proposal;
            SetProposalQueue(proposalQueue);
            SetMember(memberAddress, member);

            LogSubmitVote(proposalIndex, Message.Sender, memberAddress, uintVote);
        }

        public void ProcessProposal(ulong proposalIndex)
        {
            var proposalQueue = GetProposalQueue();

            Assert(proposalIndex < (uint)proposalQueue.Length, "Moloch::processProposal - proposal does not exist");
            Proposal proposal = proposalQueue[proposalIndex];

            Assert(GetCurrentPeriod() >= proposal.StartingPeriod.Add(VotingPeriodLength).Add(GracePeriodLength), "Moloch::processProposal - proposal is not ready to be processed");
            Assert(proposal.Processed == false, "Moloch::processProposal - proposal has already been processed");
            Assert(proposalIndex == 0 || proposalQueue[proposalIndex.Sub(1)].Processed, "Moloch::processProposal - previous proposal must be processed");

            proposal.Processed = true;
            TotalSharesRequested = TotalSharesRequested.Sub(proposal.SharesRequested);

            bool didPass = proposal.YesVotes > proposal.NoVotes;

            // Make the proposal fail if the dilutionBound is exceeded
            if (TotalShares.Mul(DilutionBound) < proposal.MaxTotalSharesAtYesVote)
            {
                didPass = false;
            }

            // PROPOSAL PASSED
            if (didPass && !proposal.Aborted)
            {
                proposal.DidPass = true;

                // todo: null check on many of the serialization getters and setters?
                // if the applicant is already a member, add to their existing shares
                if (GetMember(proposal.Applicant).Exists)
                {
                    var applicant = GetMember(proposal.Applicant);
                    applicant.Shares = applicant.Shares.Add(proposal.SharesRequested);

                    SetMember(proposal.Applicant, applicant);
                    // the applicant is a new member, create a new record for them
                }
                else
                {
                    // if the applicant address is already taken by a member's delegateKey, reset it to their member address
                    var delegateAddress = GetMemberAddressByDelegateKey(proposal.Applicant);
                    var applicant = GetMember(delegateAddress);
                    if (applicant.Exists)
                    {
                        Address memberToOverride = delegateAddress;
                        SetMemberAddressByDelegateKey(memberToOverride, memberToOverride);
                        var member = GetMember(memberToOverride);
                        member.DelegateKey = memberToOverride;
                        SetMember(memberToOverride, member);
                    }

                    // use applicant address as delegateKey by default
                    SetMember(proposal.Applicant, new Member() { DelegateKey = proposal.Applicant, Shares = proposal.SharesRequested, Exists = true, HighestIndexYesVote = 0 });
                    SetMemberAddressByDelegateKey(proposal.Applicant, proposal.Applicant);
                }

                // mint new shares
                TotalShares = TotalShares.Add(proposal.SharesRequested);

                // transfer tokens to guild bank
                Assert(
                    ApprovedStandardToken.TransferTo(GuildBank.GetAddress(), proposal.TokenTribute),
                    "Moloch::processProposal - token transfer to guild bank failed"
                );

                // PROPOSAL FAILED OR ABORTED
            }
            else
            {
                // return all tokens to the applicant
                Assert(
                    ApprovedStandardToken.TransferTo(proposal.Applicant, proposal.TokenTribute),
                    "Moloch::processProposal - failing vote token transfer failed"
                );
            }

            // send msg.sender the processingReward
            Assert(
                ApprovedStandardToken.TransferTo(Message.Sender, ProcessingReward),
                "Moloch::processProposal - failed to send processing reward to msg.sender"
            );

            // return deposit to proposer (subtract processing reward)
            Assert(
                ApprovedStandardToken.TransferTo(proposal.Proposer, ProposalDeposit.Sub(ProcessingReward)),
                        "Moloch::processProposal - failed to return proposal deposit to proposer"
                    );

            LogProcessProposal(
                proposalIndex,
                proposal.Applicant,
                proposal.Proposer,
                proposal.TokenTribute,
                proposal.SharesRequested,
                didPass
            );
        }


        public void Ragequit(ulong sharesToBurn)
        {
            AssertOnlyMember();

            ulong initialTotalShares = TotalShares;

            Member member = GetMember(Message.Sender);

            Assert(member.Shares >= sharesToBurn, "Moloch::ragequit - insufficient shares");

            Assert(canRagequit(member.HighestIndexYesVote), "Moloch::ragequit - cant ragequit until highest index proposal member voted YES on is processed");

            // burn shares
            member.Shares = member.Shares.Sub(sharesToBurn);
            TotalShares = TotalShares.Sub(sharesToBurn);

            SetMember(Message.Sender, member);

            // instruct guildBank to transfer fair share of tokens to the ragequitter
            Assert(
                GuildBank.Withdraw(Message.Sender, sharesToBurn, initialTotalShares),
                "Moloch::ragequit - withdrawal of tokens from guildBank failed"
            );

            LogRagequit(Message.Sender, sharesToBurn);
        }


        public void Abort(ulong proposalIndex)
        {
            var proposalQueue = GetProposalQueue();
            Assert(proposalIndex < (ulong)proposalQueue.Length, "Moloch::abort - proposal does not exist");
            Proposal proposal = proposalQueue[proposalIndex];

            Assert(Message.Sender == proposal.Applicant, "Moloch::abort - msg.sender must be applicant");
            Assert(GetCurrentPeriod() < proposal.StartingPeriod.Add(AbortWindow), "Moloch::abort - abort window must not have passed");
            Assert(!proposal.Aborted, "Moloch::abort - proposal must not have already been aborted");

            ulong tokensToAbort = proposal.TokenTribute;
            proposal.TokenTribute = 0;
            proposal.Aborted = true;

            proposalQueue[proposalIndex] = proposal;
            SetProposalQueue(proposalQueue);

            // return all tokens to the applicant
            Assert(
                ApprovedStandardToken.TransferTo(proposal.Applicant, tokensToAbort),
                "Moloch::processProposal - failed to return tribute to applicant"
            );

            LogAbort(proposalIndex, Message.Sender);
        }


        public void UpdateDelegateKey(Address newDelegateKey)
        {
            AssertOnlyMember();

            Assert(newDelegateKey != Address.Zero, "Moloch::updateDelegateKey - newDelegateKey cannot be 0");

            // skip checks if member is setting the delegate key to their member address
            if (newDelegateKey != Message.Sender)
            {
                Assert(!GetMember(newDelegateKey).Exists, "Moloch::updateDelegateKey - cant overwrite existing members");
                Assert(!GetMember(GetMemberAddressByDelegateKey(newDelegateKey)).Exists, "Moloch::updateDelegateKey - cant overwrite existing delegate keys");
            }

            Member member = GetMember(Message.Sender);
            SetMemberAddressByDelegateKey(member.DelegateKey, Address.Zero);
            SetMemberAddressByDelegateKey(newDelegateKey, Message.Sender);
            member.DelegateKey = newDelegateKey;
            SetMember(Message.Sender, member);

            LogUpdateDelegateKey(Message.Sender, newDelegateKey);
        }

        private ulong Max(ulong x, ulong y)
        {
            return x >= y ? x : y;
        }

        public ulong GetCurrentPeriod()
        {
            var ticksAfterSummoning = ((ulong)DateTime.UtcNow.Ticks).Sub((ulong)SummoningTime);
            return ticksAfterSummoning.Div(PeriodDuration);
        }

        public ulong GetProposalQueueLength()
        {
            return (ulong)GetProposalQueue().Length;
        }

        // can only ragequit if the latest proposal you voted YES on has been processed
        public bool canRagequit(ulong highestIndexYesVote)
        {
            var proposalQueue = GetProposalQueue();
            Assert(highestIndexYesVote < (uint)proposalQueue.Length, "Moloch::canRagequit - proposal does not exist");

            return proposalQueue[highestIndexYesVote].Processed;
        }

        public bool HasVotingPeriodExpired(ulong startingPeriod)
        {
            return GetCurrentPeriod() >= SafeMath.Add(startingPeriod, VotingPeriodLength);
        }

        public Vote GetMemberProposalVote(Address memberAddress, ulong proposalIndex)
        {
            Assert(GetMember(memberAddress).Exists, "Moloch::getMemberProposalVote - member doesn't exist");

            var proposalQueue = GetProposalQueue();
            Assert(proposalIndex < (ulong)proposalQueue.Length, "Moloch::getMemberProposalVote - proposal doesn't exist");

            // todo: see if assert is needed or we need to return Vote.Null
            Assert(proposalQueue[proposalIndex].VotesByMember.TryGetValue(memberAddress, out Vote vote), "Moloch::getMemberProposalVote - member vote does not exist");

            return vote;
        }

        // todo: find more optimal way to serialize to contract state for object types.
        public static byte[] Serialize<T>(T data) where T : class
        {
            var formatter = new BinaryFormatter();
            using (var stream = new MemoryStream())
            {
                formatter.Serialize(stream, data);
                return stream.ToArray();
            }
        }
        public static T Deserialize<T>(byte[] array) where T : class
        {
            using (var stream = new MemoryStream(array))
            {
                var formatter = new BinaryFormatter();
                return (T)formatter.Deserialize(stream);
            }
        }
    }
}