using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace Aphelion_ICO
{
    public class AphelionICO : SmartContract
    {
        //KEYS used on the storage thru this contract
        private static string KEY_OWNER() => "owner";
        private static string KEY_TOTAL_SUPPLY() => "totalSupply";

        //token settings
        private static string SETTINGS_NAME() => "Aphelion";
        private static string SETTINGS_SYMBOL() => "APH";
        public static byte SETTINGS_DECIMAL() => 8;
        private const uint factor = 100000000; //decided by SETTINGS_DECIMAL()

        private static uint round1_total_tokens = 10000000;
        private static uint round2_total_tokens = 20000000;
        private static uint round3_total_tokens = 70000000;
        private static uint round4_total_tokens = 120000000;
        private static uint round5_total_tokens = 170000000;

        //Oct 25 2017
        private static BigInteger ico_start_date = 1508889600;

        // 1 day * 24 hours * 60 mins * 60 secs after the ico start date
        private static BigInteger round1_end_time = 86400;

        // 3 days * 24 hours * 60 mins * 60 secs after the ico start date
        private static BigInteger round2_end_time = 259200;

        // 7 days * 24 hours * 60 mins * 60 secs after the ico start date
        private static BigInteger round3_end_time = 604800;

        // 14 days * 24 hours * 60 mins * 60 secs after the ico start date
        private static BigInteger round4_end_time = 1209600;

        //the total duration for the whole ico token generation
        // 21 days * 24 hours * 60 mins * 60 secs after the ico start date
        private static BigInteger ico_duration = 1814400;


        //this is the initial method that gets called when anyone invokes this contract
        public static Object Main(string operation, params object[] args)
        {
            if (!VerifyWithdrawal(operation)) return false;
            if (operation == "mintTokens") return MintTokens();
            if (operation == "totalSupply") return TotalSupply();
            if (operation == "name") return SETTINGS_NAME();
            if (operation == "symbol") return SETTINGS_SYMBOL();
            if (operation == "transfer") return Transfer(args);
            if (operation == "balanceOf") return BalanceOf(args);
            if (operation == "deploy") return Deploy();
            if (operation == "refund") return Refund();
            if (operation == "withdrawal") return Withdrawal(args);
            if (operation == "decimals") return SETTINGS_DECIMAL();

            //We believe this is a variable used for inflation.
            if (operation == "inflation") return 0;

            //We believe this is a variable for the inflationRate, which could be specified in the Neo-Gui
            if (operation == "inflationRate") return 0;

            //We believe this is the date when the inflationRate would kick in.
            if (operation == "inflationDate") return 0;

            return false;
        }

        // This should only be invoked once. In the Neo-Gui on the Peter Lin RPX branch, there is a method
        // on the ExecuteDialog, which enables the user to invoke Deploy.
        private static bool Deploy()
        {
            //TODO: We need to verify what key or signature is used for withdrawal
            //This is a byte array of the public key of the owner of the contract
            byte[] owner = new byte[] { 2, 133, 234, 182, 95, 74, 1, 38, 228, 184, 91, 78, 93, 139, 126, 48, 58, 255, 126, 251, 54, 13, 89, 95, 46, 49, 137, 187, 144, 72, 122, 213, 170 };

            //This is the amount that we would allocate before the ICO. Right now,
            //this is set to 30,000,000
            BigInteger pre_ico_cap = 30000000;

            //This is to enable us to manage the decimals as a BigInteger so we don't lose precision
            uint decimals_rate = factor;

            //We ask the Storage, which is the storage within the context of the Contract that resides on the
            //Blockchain to get the totalSupply amount within this contract.
            byte[] total_supply = Storage.Get(Storage.CurrentContext, KEY_TOTAL_SUPPLY());

            //If the total_supply is already set, then we exit with false. The totalSupply can only
            //be set once.
            if (total_supply.Length != 0)
            {
                return false;
            }

            //We write to storage that the owner address has pre_ico_cap * decimal_rate Aphelion.
            //We believe this is done to enable pre-ico reserves to be maintained.
            //TODO: verify if the owner can somehow transfer this initial amount of APH easily
            Storage.Put(Storage.CurrentContext, owner, IntToBytes(pre_ico_cap * decimals_rate));
            Storage.Put(Storage.CurrentContext, KEY_OWNER(), owner);

            //We then set the totalSupply to be equal to the pre_ico_cap * decimals_rate
            Storage.Put(Storage.CurrentContext, KEY_TOTAL_SUPPLY(), IntToBytes(pre_ico_cap * decimals_rate));

            //at this point, we have to items written to storage. We have the owner assigned to pre_ico_cap * decimals_rate,
            //and we have the totalSupply equal to the pre_ico_cap * decimals_rate. We have 30,000,000 tokens in the system
            //after calling deploy
            return true;
        }

        // This is a private function that we believe will only enable the owner to withdraw the NEO from the contract to their own wallet.
        private static bool Withdrawal(object[] args)
        {
            //this function only supports one argument
            if (args.Length != 1)
            {
                return false;
            }

            //we're assuming the one argument is the signature. what if it's not?
            byte[] signature = (byte[])args[0]; //let's get the signature

            //we get the the value of the "owner" from the storage that resides in the contract.
            byte[] owner = Storage.Get(Storage.CurrentContext, KEY_OWNER());

            //return true and allow withdrawal of NEO only if the owner of the
            //contract is the one calling this function.
            return VerifySignature(owner, signature);
        }

        // When people want to buy Aphelion using NEO, they "send" NEO to this contract.
        // The MintTokens function gets invoked to mint the number of Aphelion tokens proportional to the
        // amount of Neo sent to the contract. This function
        // can only be called during the tokenswap period
        private static bool MintTokens()
        {
            uint decimals_rate = factor;

            //We specify the Neo Global Asset Public Key so that we can ensure that only Neo is used in the transaction.
            //This is the public key for the NEO Asset. This key iss unique to the whole blockchain.
            byte[] neo_asset_id = new byte[] { 197, 111, 51, 252, 110, 207, 205, 12, 34, 92, 74, 179, 86, 254, 229, 147, 144, 175, 133, 96, 190, 14, 147, 15, 174, 190, 116, 166, 218, 255, 124, 155 };

            //We acquire a transaction from the Execution Engine
            Transaction trans = (Transaction)ExecutionEngine.ScriptContainer;

            //Is this the UTXO Approach. The other approach is what the banks use may be cleaner.
            //https://storeofvalue.github.io/posts/neo-vs-qtum-which-is-the-real-chinese-ethereum/

            //We acquire the transaction_input from the transaction
            TransactionInput trans_input = trans.GetInputs()[0];

            //We acquire the previous hash code or the transaction input
            byte[] prev_hash = trans_input.PrevHash;

            //We acquire the new transaction from the blockchain using the previous_hash as the input.
            Transaction prev_trans = Blockchain.GetTransaction(prev_hash);

            //We acquire the TransactionOutput by using the transaction inputs previous index as the index into the
            //previous_transactions output
            TransactionOutput prev_trans_output = prev_trans.GetOutputs()[trans_input.PrevIndex];

            //This enables us to see whether the asset that was passed to us in the previous transaction was Neo.
            //We see if the previous_transaction output asset id is equal to the neo_asset_id we specified at the top.
            if (!BytesEqual(prev_trans_output.AssetId, neo_asset_id))
            {
                //If they didn't pass Neo to this transaction, then we exit.
                return false;
            }
            //We acquire the ScriptHash from the previous transaction output. The Script Hash is equal to public key, which
            //tells us who it is.
            byte[] sender = prev_trans_output.ScriptHash;

            //We go to the current transactoins output.
            TransactionOutput[] trans_outputs = trans.GetOutputs();

            //We acquire the ScriptHash of the user who is Executing the transaction. The ScriptHash is the Public key.
            byte[] receiver = ExecutionEngine.ExecutingScriptHash;

            //We setup a value variable to store the amount of Neo that is coming in
            long value = 0;

            //We iterate through the Transaction Outputs
            foreach (TransactionOutput trans_output in trans_outputs)
            {
                //We ensure ensure that the receiver is equal to the public key of the transaction output.
                if (BytesEqual(trans_output.ScriptHash, receiver))
                {
                    //add the transaction_output value of Neo to the value variable
                    value += trans_output.Value;

                }
            }

            //Get the current exchange rate
            uint swap_rate = CurrentSwapRate();

            //If the current exchange rate is 0, then it's a refund. We want to log who gets the refund.
            if (swap_rate == 0)
            {
                //We acquire the public key for the log of the refundes
                byte[] refund = Storage.Get(Storage.CurrentContext, "refund");

                //We convert the amount of Neos to be refunded from the value that we acquired by iterating through the output transactions
                //to an integer
                byte[] sender_value = IntToBytes(value);

                //We take the refunds, and we actually concatenate the senders address to the refund log on the blockchain
                byte[] new_refund = refund.Concat(sender.Concat(IntToBytes(sender_value.Length).Concat(sender_value)));

                //We write the new_refund log to the "refund" entity in the storage of the contract
                Storage.Put(Storage.CurrentContext, "refund", new_refund);
                return false;
            }

            //We now calculate the amount of Aphelion that this person is purchasing.
            //We multiply the sum of the transaction output values by the current exchange rate by the decimals_rate
            long token = value * swap_rate * decimals_rate;

            //We acquire the send value from the contract storage. We associate that with a BigInteger to symbolize the current
            //amount of Aphelion in the system for this user.
            BigInteger total_token = BytesToInt(Storage.Get(Storage.CurrentContext, sender));

            //We then sum the current amount of aphelion plus the new amount of aphelion with transaction, and we write to the
            //storage of the contract
            Storage.Put(Storage.CurrentContext, sender, IntToBytes(token + total_token));

            //Next, we acquire the totalSupply from the contract's storage
            byte[] totalSupply = Storage.Get(Storage.CurrentContext, KEY_TOTAL_SUPPLY());

            //We increment the totalSupply by the amount purchased.
            Storage.Put(Storage.CurrentContext, KEY_TOTAL_SUPPLY(), IntToBytes(token + BytesToInt(totalSupply)));

            //The transaction is complete.
            return true;
        }

        //Get the log of people who have asked for refunds
        private static byte[] Refund()
        {
            return Storage.Get(Storage.CurrentContext, "refund");
        }

        //Get the total token supply
        private static BigInteger TotalSupply()
        {
            byte[] totalSupply = Storage.Get(Storage.CurrentContext, KEY_TOTAL_SUPPLY());
            return BytesToInt(totalSupply);
        }

        //Transfer APH tokens from the contract to a public address.
        //For NEP5 compliance, the params should be:
        //  The first index contains the from public key and type is byte[],
        //  The second index contains the receiver address and type is byte[],
        //  The third index contains the number of token and type is BigInteger .
        private static bool Transfer(object[] args)
        {
            if (args.Length != 3)
              return false;

            // we get the sender address here.
            byte[] from = (byte[])args[0];

            //Need to ensure the sender address is the one doing the transfer.
            if (!Runtime.CheckWitness(from))
              return false;

            //now we get the addres to send the tokens here
            byte[] to = (byte[])args[1];

            //The amount of APH to be transfered
            BigInteger value = BytesToInt((byte[])args[2]);

            // return if no APHs are being transfered
            if (value < 0)
              return false;

            //get the amount of APH for the FROM account here
            byte[] from_value = Storage.Get(Storage.CurrentContext, from);

            // get the amount of APH for the TO account here.
            byte[] to_value = Storage.Get(Storage.CurrentContext, to);

            //get the new value that would be on the FROM account
            BigInteger n_from_value = BytesToInt(from_value) - value;

             //if that new value iz zero, return, the transfer can't be done
            if (n_from_value < 0)
              return false;

            //now let's get the new value that would be on the TO account
            BigInteger n_to_value = BytesToInt(to_value) + value;

            //update the FROM account to the new value
            Storage.Put(Storage.CurrentContext, from, IntToBytes(n_from_value));

            //update the TO account to the new value
            Storage.Put(Storage.CurrentContext, to, IntToBytes(n_to_value));

            //fire the transfered event, to comply with NEP-5
            Transferred(args);
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
            //if no arguments are found, return
            if (args.Length != 1)
              return 0;

            //get the address for the account in question
            byte[] address = (byte[])args[0];

            //get the amount of APH for that account from the storage
            byte[] balance = Storage.Get(Storage.CurrentContext, address);

            //return the APH
            return BytesToInt(balance);
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
            uint height = Blockchain.GetHeight();
            uint now = Blockchain.GetHeader(height).Timestamp;
            uint time = (uint)ico_start_date - now;
            uint exchange_rate = 1000;
            BigInteger total_amount = 1000000000; //this is the amount at which we'll stop generating APH
            byte[] total_supply = Storage.Get(Storage.CurrentContext, KEY_TOTAL_SUPPLY());
            
            if (time < 0)
            {
                return 0;
            }
            else if (time <= round1_end_time && BytesToInt(total_supply) < round1_total_tokens)
            {
                //return the preico exchange rate here
                return exchange_rate * 150 / 100;
            }
            else if (time > round1_end_time &&  time <= round2_end_time && BytesToInt(total_supply) < round2_total_tokens)
            {
                //return the preico exchange rate here
                return exchange_rate * 140 / 100;
            }
            else if (time > round2_end_time && time <= round3_end_time && BytesToInt(total_supply) < round3_total_tokens)
            {
                //return the preico exchange rate here
                return exchange_rate * 130 / 100;
            }
            else if (time > round3_end_time && time <= round4_end_time && BytesToInt(total_supply) < round4_total_tokens)
            {
                //return the preico exchange rate here
                return exchange_rate * 120 / 100;
            }
            else if (time > round4_end_time && time <= ico_duration && BytesToInt(total_supply) < round5_total_tokens)
            {
                //return the preico exchange rate here
                return exchange_rate;
            }
            else
            {
                return 0;
            }

        }
    }
}
