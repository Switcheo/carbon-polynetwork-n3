
import { CONST, rpc, sc, tx, u } from "@cityofzion/neon-core";

export const MAGIC_NUMBER = CONST.MAGIC_NUMBER.TestNet
export const rpcClient = new rpc.RPCClient("http://127.0.0.1:20332");
export const scopes = tx.WitnessScope.Global

export async function createTransaction(scriptHash, operation, args, fromAccount) {
  console.log("\n\n --- Creating Transaction ---");
  console.log(
    `Sending operation: ${operation} \n`+
    `to: ${scriptHash} \n` +
    `with args: ${JSON.stringify(args, null, 2)} \n`
  );

  // Since the token is now an NEP-5 token, we transfer using a VM script.
  const script = sc.createScript({
    scriptHash,
    operation,
    args,
  });

  // We retrieve the current block height as we need to
  const currentHeight = await rpcClient.getBlockCount();
  const txn = new tx.Transaction({
    signers: [
      {
        account: fromAccount.scriptHash,
        scopes,
      },
    ],
    validUntilBlock: currentHeight + 1000,
    script: script,
  });
  console.log("\u001b[32m  ✓ Transaction created \u001b[0m");

  return txn
}

export async function checkNetworkFee(networkFee, txn) {
  const feePerByteInvokeResponse = await rpcClient.invokeFunction(
    CONST.NATIVE_CONTRACT_HASH.PolicyContract,
    "getFeePerByte"
  );

  if (feePerByteInvokeResponse.state !== "HALT") {
    if (networkFee === 0) {
      throw new Error("Unable to retrieve data to calculate network fee.");
    } else {
      console.log(
        "\u001b[31m  ✗ Unable to get information to calculate network fee.  Using user provided value.\u001b[0m"
      );
      txn.networkFee = u.BigInteger.fromNumber(networkFee);
    }
  }
  const feePerByte = u.BigInteger.fromNumber(feePerByteInvokeResponse.stack[0].value)
  // Account for witness size
  const transactionByteSize = txn.serialize().length / 2 + 109;
  // Hardcoded. Running a witness is always the same cost for the basic account.
  const witnessProcessingFee = u.BigInteger.fromNumber(1000390);
  const networkFeeEstimate = feePerByte
    .mul(transactionByteSize)
    .add(witnessProcessingFee);
  if (networkFee && networkFee >= networkFeeEstimate.toNumber()) {
    txn.networkFee = u.BigInteger.fromNumber(networkFee);
    console.log(
      `  i Node indicates ${networkFeeEstimate.toDecimal(8)} networkFee but using user provided value of ${
        networkFee
      }`
    );
  } else {
    txn.networkFee = networkFeeEstimate;
  }
  console.log(
    `\u001b[32m  ✓ Network Fee set: ${txn.networkFee.toDecimal(8)} \u001b[0m`
  );

  return txn
}

export async function checkToken(tokenScriptHash) {
  const tokenNameResponse = await rpcClient.invokeFunction(
    tokenScriptHash,
    "symbol"
  );

  if (tokenNameResponse.state !== "HALT") {
    throw new Error(
      "Token not found! Please check the provided tokenScriptHash is correct."
    );
  }

  const tokenName = u.HexString.fromBase64(
    tokenNameResponse.stack[0].value
  ).toAscii();

  console.log("\u001b[32m  ✓ Token found: " + tokenName + " \u001b[0m");
}

export async function checkSystemFee(systemFee, fromAccount, txn) {
  const invokeFunctionResponse = await rpcClient.invokeScript(u.HexString.fromHex(txn.script), [
    {
      account: fromAccount.scriptHash,
      scopes,
    },
  ]);
  if (invokeFunctionResponse.state !== "HALT") {
    throw new Error(
      `Simulation errored out: ${invokeFunctionResponse.exception}`
    );
  }
  const requiredSystemFee = u.BigInteger.fromNumber(
    invokeFunctionResponse.gasconsumed
  );
  if (systemFee && systemFee >= requiredSystemFee) {
    txn.systemFee = u.BigInteger.fromNumber(systemFee);
    console.log(
      `  i Node indicates ${requiredSystemFee} systemFee but using user provided value of ${systemFee}`
    );
  } else {
    txn.systemFee = requiredSystemFee;
  }
  console.log(
    `\u001b[32m  ✓ SystemFee set: ${txn.systemFee.toDecimal(8)}\u001b[0m`
  );

  return txn
}

export async function checkBalance(fromAccount, tokenScriptHash, amountToTransfer, txn) {
  let balanceResponse;
  try {
    balanceResponse = await rpcClient.execute(new rpc.Query({
      method: "getnep17balances",
      params: [fromAccount.address],
    }));
  } catch (e) {
    console.log(e)
    console.log(
      "\u001b[31m  ✗ Unable to get balances as plugin was not available. \u001b[0m"
    );
    return;
  }
  // Check for token funds
  const balances = balanceResponse.balance.filter((bal) =>
    bal.assethash.includes(tokenScriptHash)
  );
  const balanceAmount =
    balances.length === 0 ? 0 : parseInt(balances[0].amount);
  if (balanceAmount < amountToTransfer) {
    throw new Error(`Insufficient funds! Found ${balanceAmount}`);
  } else {
    console.log("\u001b[32m  ✓ Token funds found \u001b[0m");
  }

  // Check for gas funds for fees
  const gasRequirements = txn.networkFee.add(
    txn.systemFee
  );
  const gasBalance = balanceResponse.balance.filter((bal) =>
    bal.assethash.includes(CONST.NATIVE_CONTRACT_HASH.GasToken)
  );
  const gasAmount =
    gasBalance.length === 0
      ? u.BigInteger.fromNumber(0)
      : u.BigInteger.fromNumber(gasBalance[0].amount);

  if (gasAmount.compare(gasRequirements) === -1) {
    throw new Error(
      `Insufficient gas to pay for fees! Required ${gasRequirements.toString()} but only had ${gasAmount.toString()}`
    );
  } else {
    console.log(
      `\u001b[32m  ✓ Sufficient GAS for fees found (${gasRequirements.toString()}) \u001b[0m`
    );
  }
}

export async function performTransaction(fromAccount, txn) {
  const signedTransaction = txn.sign(
    fromAccount,
    MAGIC_NUMBER,
  );

  console.log(txn.toJson());
  const result = await rpcClient.sendRawTransaction(
    u.HexString.fromHex(signedTransaction.serialize(true))
  );

  console.log("\n\n--- Transaction hash ---");
  console.log(result);
}