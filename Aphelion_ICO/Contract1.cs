using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace Aphelion_ICO
{
    public class Contract1 : SmartContract
    {
        //this is the initial method that gets called when anyone invokes this contract
        public static Object Main(string operation, params object[] args)
        {
            string name = "Aphelion";
            string symbol = "APH";
            BigInteger decimals = 8;
            if (!VerifyWithdrawal(operation)) return false;
            if (operation == "mintTokens") return MintTokens();
            if (operation == "totalSupply") return TotalSupply();
            if (operation == "name") return name;
            if (operation == "symbol") return symbol;
            if (operation == "transfer") return Transfer(args);
            if (operation == "balanceOf") return BalanceOf(args);
            if (operation == "deploy") return Deploy();
            if (operation == "refund") return Refund();
            if (operation == "withdrawal") return Withdrawal(args);
            if (operation == "decimals") return decimals;
            if (operation == "inflation") return 0; //no idea if we need this.
            if (operation == "inflationRate") return 0; //no idea if we need this
            if (operation == "inflationDate") return 0; //no idea if we need this

            return false;
        }
        // initialization parameters, only once
        private static bool Deploy()
        {
            //this is a byte array of the key of the owner
            //TODO: verify if public key, private key, signature or what works here for witdrawal. 
            byte[] owner = new byte[] { 2, 133, 234, 182, 95, 74, 1, 38, 228, 184, 91, 78, 93, 139, 126, 48, 58, 255, 126, 251, 54, 13, 89, 95, 46, 49, 137, 187, 144, 72, 122, 213, 170 };
            BigInteger pre_ico_cap = 30000000;
            uint decimals_rate = 100000000;
            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if (total_supply.Length != 0)
            {
                return false;
            }
            //TODO: verify if the owner can somehow transfer this initial amount of APH easily
            Storage.Put(Storage.CurrentContext, owner, IntToBytes(pre_ico_cap * decimals_rate)); //the idea is to generate in initial supply based on the supply needed
                                                                                                    //for the pre-ico supporters?
            Storage.Put(Storage.CurrentContext, "totalSupply", IntToBytes(pre_ico_cap * decimals_rate)); //this would be the initial supply. How do we transfer this to the pre-ico supporters?
            return true;
        }
        // The function Withdrawal is only usable when contract owner want
        // to transfer neo from contract
        private static bool Withdrawal(object[] args)
        {
            if (args.Length != 1)
            {
                return false;
            }
            byte[] signature = (byte[])args[0]; //let's get the signature
            byte[] owner = Storage.Get(Storage.CurrentContext, "owner"); //get the owner signature of this contract
            return VerifySignature(owner, signature); //return true and allow withdrawal of NEO only if the owner of the 
                                                        //contract is the one calling this function.
        }

        // The function MintTokens is only usable by the chosen wallet
        // contract to mint a number of tokens proportional to the
        // amount of neo sent to the wallet contract. The function
        // can only be called during the tokenswap period
        private static bool MintTokens()
        {
            uint decimals_rate = 100000000;
            //this is the id of the NEO Asset. it's unique to the whole blockchain.
            byte[] neo_asset_id = new byte[] { 197, 111, 51, 252, 110, 207, 205, 12, 34, 92, 74, 179, 86, 254, 229, 147, 144, 175, 133, 96, 190, 14, 147, 15, 174, 190, 116, 166, 218, 255, 124, 155 };
            Transaction trans = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionInput trans_input = trans.GetInputs()[0];
            byte[] prev_hash = trans_input.PrevHash;
            Transaction prev_trans = Blockchain.GetTransaction(prev_hash);
            TransactionOutput prev_trans_output = prev_trans.GetOutputs()[trans_input.PrevIndex];
            if (!BytesEqual(prev_trans_output.AssetId, neo_asset_id))
            {
                return false; //return false if, for some reason, the hash for this transaction is the same as the previous one.
            }
            byte[] sender = prev_trans_output.ScriptHash;
            TransactionOutput[] trans_outputs = trans.GetOutputs();
            byte[] receiver = ExecutionEngine.ExecutingScriptHash;
            //La suma de los bytes es la dirección del contrato actual ?
            long value = 0;
            foreach (TransactionOutput trans_output in trans_outputs)
            {
                if (BytesEqual(trans_output.ScriptHash, receiver))
                {
                    value += trans_output.Value;

                }
            }
            //get the current rate and, then, calculate the real amount of tokens issued.
            uint swap_rate = CurrentSwapRate();
            if (swap_rate == 0)
            {
                byte[] refund = Storage.Get(Storage.CurrentContext, "refund");
                byte[] sender_value = IntToBytes(value);
                byte[] new_refund = refund.Concat(sender.Concat(IntToBytes(sender_value.Length).Concat(sender_value)));
                Storage.Put(Storage.CurrentContext, "refund", new_refund);
                return false;
            }
            long token = value * swap_rate * decimals_rate;
            //store the APH into the account and add it to the total supply.
            BigInteger total_token = BytesToInt(Storage.Get(Storage.CurrentContext, sender));
            Storage.Put(Storage.CurrentContext, sender, IntToBytes(token + total_token));
            byte[] totalSypply = Storage.Get(Storage.CurrentContext, "totalSypply");
            Storage.Put(Storage.CurrentContext, "totalSypply", IntToBytes(token + BytesToInt(totalSypply)));
            return true;
        }
        // Get the storage values saved for all the refunds pending
        private static byte[] Refund()
        {
            return Storage.Get(Storage.CurrentContext, "refund");
        }
        // Get the total token supply
        private static BigInteger TotalSupply()
        {
            byte[] totalSupply = Storage.Get(Storage.CurrentContext, "totalSypply");
            return BytesToInt(totalSupply);
        }

        //Transfer APH tokens from my account to a public address.
        //For NEP5 compliance, the params should be:
        //The first element is sender address and type is byte[], 
        //the second element is receiver address and type is byte[], 
        //the third element is the number of token and type is BigInteger .
        private static bool Transfer(object[] args)
        {
            if (args.Length != 3) return false;
            byte[] from = (byte[])args[0]; // we get the sender address here. 
            if (!Runtime.CheckWitness(from)) return false; //Need to ensure the sender address is the one doing the transfer.
            byte[] to = (byte[])args[1]; //now we get the addres to send the tokens here
            BigInteger value = BytesToInt((byte[])args[2]); //The amount of APH to be transfered
            if (value < 0) return false; // return if no APHs are being transfered
            byte[] from_value = Storage.Get(Storage.CurrentContext, from); //get the amount of APH for the FROM account here
            byte[] to_value = Storage.Get(Storage.CurrentContext, to); // get the amount of APH for the TO account here.
            BigInteger n_from_value = BytesToInt(from_value) - value; //get the new value that would be on the FROM account
            if (n_from_value < 0) return false; //if that new value iz zero, return, the transfer can't be done
            BigInteger n_to_value = BytesToInt(to_value) + value; //now let's get the new value that would be on the TO account
            Storage.Put(Storage.CurrentContext, from, IntToBytes(n_from_value)); //update the FROM account to the new value
            Storage.Put(Storage.CurrentContext, to, IntToBytes(n_to_value)); //update the TO account to the new value
            Transferred(args); //fire the transfered event, to comply with NEP-5
            return true; 
        }

        //This event is fired when a succesful transfer is made. 
        //For NEP5 compliance, the params should be:
        //The first element is sender address and type is byte[], 
        //the second element is receiver address and type is byte[], 
        //the third element is the number of token and type is BigInteger .
        private static void Transferred(object[] args)
        {
            Runtime.Notify(args);
        }

        //Get the balance of APH in a given address
        //For NEP5 compliance, the params should be:
        //The first element is the address of the account and type is byte[]
        private static BigInteger BalanceOf(object[] args)
        {
            if (args.Length != 1) return 0; //if no arguments are found, return
            byte[] address = (byte[])args[0]; //get the address for the account in question
            byte[] balance = Storage.Get(Storage.CurrentContext, address); //get the amount of APH for that account from the storage
            return BytesToInt(balance); //return the APH
        }


        //helper function that parses Bytes to BigInteger
        private static BigInteger BytesToInt(byte[] array)
        {
            var buffer = new BigInteger(array);
            return buffer;
        }

        //helper function that parses BigInteger to bytes
        private static byte[] IntToBytes(BigInteger value)
        {
            byte[] buffer = value.ToByteArray();
            return buffer;
        }

        //helper function that compares if 2 arrays of bytes are equal
        private static bool BytesEqual(byte[] b1, byte[] b2)
        {
            if (b1.Length != b2.Length) return false;
            for (int i = 0; i < b1.Length; i++)
                if (b1[i] != b2[i])
                    return false;
            return true;
        }

        // transfer neo in smart contract can only invoke
        // the function Withdrawal
        private static bool VerifyWithdrawal(string operation)
        {
            if (operation == "withdrawal")
            {
                return true;
            }
            Transaction trans = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionInput trans_input = trans.GetInputs()[0];
            Transaction prev_trans = Blockchain.GetTransaction(trans_input.PrevHash);
            TransactionOutput prev_trans_output = prev_trans.GetOutputs()[trans_input.PrevIndex];
            byte[] script_hash = ExecutionEngine.ExecutingScriptHash;
            if (BytesEqual(prev_trans_output.ScriptHash, script_hash))
            {
                return false;
            }
            return true;
        }
        // The function CurrentSwapRate() returns the current exchange rate
        // between rpx tokens and neo during the token swap period
        private static uint CurrentSwapRate()
        {
            BigInteger ico_start_time = 1508889600; //Oct 25 2017
            BigInteger ico_end_time = 1511568000; //Nov 25 2017
            uint exchange_rate = 1000;
            BigInteger total_amount = 1000000000;
            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if (BytesToInt(total_supply) > total_amount)
            {
                return 0;
            }
            uint height = Blockchain.GetHeight();
            uint now = Blockchain.GetHeader(height).Timestamp;
            uint time = (uint)ico_start_time - now;
            if (time < 0)
            {
                return 0;
            }
            else if (time <= 86400)
            {
                return exchange_rate * 130 / 100;
            }
            else if (time <= 259200)
            {
                return exchange_rate * 120 / 100;
            }
            else if (time <= 604800)
            {
                return exchange_rate * 110 / 100;
            }
            else if (time <= 1209600)
            {
                return exchange_rate;
            }
            else
            {
                return 0;
            }

        }
    }
}
