# Carbon - Neo3 Contracts

This is the repository contains the  Neo (3.0) deposit and wrapped native token (SWTH) contracts for Carbon via the Polynetwork bridge.

## Deployment

1. Install [.NET 6.0 SDK](https://dotnet.microsoft.com/download)
2. Install contract compiler and templates: `dotnet new -i Neo3.SmartContract.Templates`
3. Compile with: `dotnet build`
4. Install [neo-cli](https://docs.neo.org/docs/en-us/node/cli/setup.html)
5. Sync to latest height with: `dotnet neo-cli.dll`
6. In neo-cli, create / open a wallet: `open wallet`
7. In neo-cli, deploy with: `deploy <pathToSolution>/CrossChainProxy/bin/sc/CrossChainProxy.nef <pathToSolution>/CrossChainProxy/bin/sc/CrossChainProxy.manifest.json`

For more information, see the [Neo 3.0 docs](https://docs.neo.org/docs/en-us/gettingstarted/develop.html).

## Current deployed contracts

### Devnet

- Address: TBD
- Big Endian ScriptHash: 0xeeebee7ef57cb2106fbad2c51c5b9b4c30f0c0ca
- Little Endian ScriptHash: TBD

### Mainnet

- Address: TBD
- Big Endian ScriptHash: TBD
- Little Endian ScriptHash: TBD

### Legacy Contracts

For Neo 2.0 contracts, check the [Neo 2.0 repo](https://github.com/Switcheo/carbon-polynetwork-neo).
