﻿using HiddenWallet.Daemon.Models;
using HiddenWallet.FullSpvWallet.ChaumianCoinJoin;
using HiddenWallet.KeyManagement;
using HiddenWallet.Models;
using HiddenWallet.SharedApi.Models;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HiddenWallet.Daemon.Controllers
{
	[Route("api/v1/[controller]")]
	public class TumblerController : Controller
    {
		[HttpGet]
		public string Test()
		{
			return "test";
		}

		[Route("connection")]
		[HttpGet]
		public async Task<IActionResult> ConnectionAsync()
		{ 
			try
			{
				CoinJoinService coinJoinService = Global.WalletWrapper.WalletJob.CoinJoinService;
				if (coinJoinService.TumblerConnection == null)
				{
					await coinJoinService.SubscribePhaseChangeAsync();
				}
				if (coinJoinService.TumblerConnection == null)
				{
					return new ObjectResult(new FailureResponse { Message = "", Details = "" });
				}
				else
				{
					if(coinJoinService.StatusResponse == null)
					{
						coinJoinService.StatusResponse = await coinJoinService.TumblerClient.GetStatusAsync(CancellationToken.None);
					}

					if(coinJoinService.TumblingInProcess)
					{
						return new ObjectResult(new ConnectionResponse { IsMixOngoing = true });
					}
					else
					{
						return new ObjectResult(new ConnectionResponse { IsMixOngoing = false });
					}
				}
			}
			catch (Exception ex)
			{
				return new ObjectResult(new FailureResponse { Message = ex.Message, Details = ex.ToString() });
			}
		}

		[Route("tumble")]
		[HttpPost]
		public async Task<IActionResult> TumbleAsync([FromBody]TumbleRequest request)
		{
			try
			{
				if (request == null || request.From == null || request.To == null || request.RoundCount == 0)
				{
					return new ObjectResult(new FailureResponse { Message = "Bad request", Details = "" });
				}

				var getFrom = Global.WalletWrapper.GetAccount(request.From, out SafeAccount fromAccount);
				if (getFrom != null) return new ObjectResult(getFrom);

				var getTo = Global.WalletWrapper.GetAccount(request.To, out SafeAccount toAccount);
				if (getTo != null) return new ObjectResult(getTo);
				
				for (int i = 0; i < request.RoundCount; i++)
				{
					IEnumerable<Script> unusedOutputs = await Global.WalletWrapper.WalletJob.GetUnusedScriptPubKeysAsync(AddressType.Pay2WitnessPublicKeyHash, toAccount, HdPathType.NonHardened);
					BitcoinAddress activeOutput = unusedOutputs.RandomElement().GetDestinationAddress(Global.WalletWrapper.Network); // TODO: this is sub-optimal, it'd be better to not which had been already registered and not reregister it
					BitcoinWitPubKeyAddress bech32 = new BitcoinWitPubKeyAddress(activeOutput.ToString(), Global.WalletWrapper.Network);

					uint256 txid = await Global.WalletWrapper.WalletJob.CoinJoinService.TumbleAsync(fromAccount, bech32, CancellationToken.None);
					while(!Global.WalletWrapper.WalletJob.MemPoolJob.Transactions.Contains(txid))
					{
						await Task.Delay(100);
					}
				}

				return new ObjectResult(new SuccessResponse());
			}
			catch(OperationCanceledException)
			{
				return new ObjectResult(new FailureResponse { Message = "Mixing was cancelled" });
			}
			catch (Exception ex)
			{
				return new ObjectResult(new FailureResponse { Message = ex.Message, Details = ex.ToString() });
			}
		}

		[Route("cancel-mix")]
		[HttpGet]
		public async Task<IActionResult> CancelMixAsync()
		{
			try
			{
				CoinJoinService coinJoinService = Global.WalletWrapper.WalletJob.CoinJoinService;
				await coinJoinService.CancelMixAsync(CancellationToken.None);
				return new ObjectResult(new SuccessResponse());
			}
			catch (Exception ex)
			{
				return new ObjectResult(new FailureResponse { Message = ex.Message, Details = ex.ToString() });
			}
		}
	}
}