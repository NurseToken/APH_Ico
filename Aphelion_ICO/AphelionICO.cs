using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Aphelion_ICO
{
    public class AphelionICO : SmartContract
    {
        //Token settings
        public static ulong development_version = 123456;
        public static string Name() => "AphelionAnd1267";
        public static string Symbol() => "APHAnd1267";
        public static byte Decimals() => 8;
        private const ulong factor = 100000000; //decided by Decimals()
        private const ulong neo_decimals = 100000000;

        //ICO Settings
        private static readonly byte[] neo_asset_id = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private const ulong basic_rate = 10 * factor;

        //This is the amount of icos raised on the preico campaign
        private const ulong preico_amount = 220000000;

        //This is the total of tokens to produce during the ico campaign
        private const ulong total_ico_amount = 440000000;

        //Nov 2, 2017
        private const uint ico_start_date = 1509580800;
        
        // 1 day * 24 hours * 60 mins * 60 secs after the ico start date
        private const uint round1_end_time = 86400;

        // 3 days * 24 hours * 60 mins * 60 secs after the ico start date
        private const uint round2_end_time = 259200;

        //the total duration for the whole ico token generation
        // 14 days * 24 hours * 60 mins * 60 secs after the ico start date
        private const ulong ico_duration = 1209600;

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> TransferredEvent;

        [DisplayName("refund")]
        public static event Action<byte[], BigInteger> RefundEvent;


        public static Object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                //this verification is run when trying to spend funds from this contract. 
                //only the owner would be able to spend the funds.
                byte[] localOwner = new byte[] { 65, 78, 82, 107, 117, 54, 55, 116, 72, 78, 111, 53, 57, 114, 122, 103, 113, 105, 86, 101, 122, 102, 99, 103, 74, 56, 107, 53, 49, 103, 51, 70, 111, 55 };
                if (localOwner.Length == 20)
                {
                    return Runtime.CheckWitness(localOwner);
                }
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == "deploy") return Deploy();
                if (operation == "listRefund") return ListRefund();
                if (operation == "mintTokens") return MintTokens();
                if (operation == "totalSupply") return TotalSupply();
                if (operation == "currentRate") return GetCurrentRate();
                if (operation == "roundTotal")
                {
                    if (args.Length != 1) return false;
                    return RoundTotal((BigInteger)args[0]);
                }
                if (operation == "name") return Name();
                if (operation == "symbol") return Symbol();
                if (operation == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Transfer(from, to, value);
                }
                if (operation == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOf(account);
                }
                if (operation == "decimals") return Decimals();
            }
            return false;
        }

        // initialization parameters, only once
        public static bool Deploy()
        {
            //Address of the owner of this contract
            byte[] localOwner = new byte[] { 65, 78, 82, 107, 117, 54, 55, 116, 72, 78, 111, 53, 57, 114, 122, 103, 113, 105, 86, 101, 122, 102, 99, 103, 74, 56, 107, 53, 49, 103, 51, 70, 111, 55 };

            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if (total_supply.Length != 0) return false;
            Storage.Put(Storage.CurrentContext, localOwner, preico_amount);
            Storage.Put(Storage.CurrentContext, "totalSupply", preico_amount);
            TransferredEvent(null, localOwner, preico_amount);
            return true;
        }

        // The function MintTokens is only usable by the chosen wallet
        // contract to mint a number of tokens proportional to the
        // amount of neo sent to the wallet contract. The function
        // can only be called during the tokenswap period
        public static bool MintTokens()
        {
            byte[] sender = GetSender();
            // contribute asset is not neo
            if (sender.Length == 0)
            {
                return false;
            }
            ulong contribute_value = GetContributeValue();

            // the current exchange rate between ico tokens and neo during the token swap period
            int current_round = GetCurrentRound();
            if (current_round < 0) {
                Refund(sender, contribute_value);
                return false;
            }

            ulong swap_rate = GetRateByRound(current_round);
            
            // you can get current swap token amount
            ulong token = GetCurrentSwapToken(sender, contribute_value, swap_rate, current_round);
            if (token <= 0)
            {
                return false;
            }

            // crowdfunding success
            BigInteger balance = Storage.Get(Storage.CurrentContext, sender).AsBigInteger();
            Storage.Put(Storage.CurrentContext, sender, token + balance);
            BigInteger totalSupply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            Storage.Put(Storage.CurrentContext, "totalSupply", token + totalSupply);
            BigInteger currentRoundSupply = Storage.Get(Storage.CurrentContext, "round-" + current_round).AsBigInteger();
            Storage.Put(Storage.CurrentContext, "round-"+current_round, currentRoundSupply + token);
            TransferredEvent(null, sender, token);
            return true;
        }

        public static void Refund(byte[] sender, BigInteger value) {
            byte[] refund = Storage.Get(Storage.CurrentContext, "refund");
            byte[] sender_value = IntToBytes(value);
            byte[] new_refund = refund.Concat(sender.Concat(IntToBytes(sender_value.Length).Concat(sender_value)));
            Storage.Put(Storage.CurrentContext, "refund", new_refund);
            RefundEvent(sender, value);
        }

        // get the total tokens generated for a round
        public static BigInteger RoundTotal(BigInteger round)
        {
            return Storage.Get(Storage.CurrentContext, "round-" + round).AsBigInteger() * factor;
        }

        // get the total token supply
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger() * factor;
        }

        // function that is always called when someone wants to transfer tokens.
        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            // Fix the value by the factor. They come up multiplied by the precision/decimal value
            value = value / factor;
            if (value <= 0) return false;            
            if (!Runtime.CheckWitness(from)) return false;
            BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (from_value < value) return false;
            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);
            BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, to_value + value);
            TransferredEvent(from, to, value);
            return true;
        }

        // get the account balance of another account with address
        public static BigInteger BalanceOf(byte[] address)
        {            
            return Storage.Get(Storage.CurrentContext, address).AsBigInteger() * factor;
        }

        // The function CurrentSwapRate() returns the current exchange rate
        // between ico tokens and neo during the token swap period
        private static int GetCurrentRound()
        {
            uint now = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp + 15 * 60;
            uint time = now - ico_start_date;
            if (time < 0) {
                return -1;
            }
            else if (time < round1_end_time)
            {
                return 0;
            }
            else if (time < round2_end_time)
            {
                return 1;
            }
            else if (time < ico_duration)
            {
                return 2;
            }
            else
            {
                return -1;
            }
        }

        //If the amount of tokens mint or neo sent goes over the total tokens on a round, 
        //swap the ones available and refund the rest. 
        private static ulong GetCurrentSwapToken(byte[] sender, ulong value, ulong swap_rate, int round)
        {
            ulong token = value / neo_decimals * swap_rate;
            BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            BigInteger balance_token = total_ico_amount - total_supply;
            if (balance_token <= 0)
            {
                Refund(sender, value);
                return 0;
            }
            else if (balance_token < token)
            {
                Refund(sender, (token - balance_token) / swap_rate * neo_decimals);
                token = (ulong)balance_token;
            }
            return token;
        }

        // check whether asset is neo and get sender script hash
        private static byte[] GetSender()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] reference = tx.GetReferences();
            // you can choice refund or not refund
            foreach (TransactionOutput output in reference)
            {
                if (output.AssetId == neo_asset_id) return output.ScriptHash;
            }
            return new byte[0];
        }

        // get smart contract script hash
        private static byte[] GetReceiver()
        {
            return ExecutionEngine.ExecutingScriptHash;
        }

        // get the value of neo that is being passed to this contract to mint tokens
        private static ulong GetContributeValue()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            // get the total amount of Neo
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == GetReceiver() && output.AssetId == neo_asset_id)
                {
                    value += (ulong)output.Value;
                }
            }
            return value;
        }

        //this method controls the rate at which neo tokens are exchanged for aphelion
        //when minting tokens
        //tried to have an array with this but the NEO compiler seems to fail with
        //int arrays
        private static ulong GetRateByRound(int round) {
            if (round == 0) return 150; //round 1 exchange rate
            if (round == 1) return 140; //round 2 exchange rates
            if (round == 2) return 120; //round 3 exchange rates
            return 0;
        }

        private static byte[] ListRefund()
        {
            return Storage.Get(Storage.CurrentContext, "refund");
        }

        // the current exchange rate between ico tokens and neo during the token swap period
        // this method is here to display the current rate on the custom dialog for the neo-gui
        private static ulong GetCurrentRate() {
            int current_round = GetCurrentRound();
            return GetRateByRound(current_round);
        }
        
        private static BigInteger BytesToInt(byte[] array)
        {
            var buffer = new BigInteger(array);
            return buffer;
        }

        private static byte[] IntToBytes(BigInteger value)
        {
            byte[] buffer = value.ToByteArray();
            return buffer;
        }

    }
}
