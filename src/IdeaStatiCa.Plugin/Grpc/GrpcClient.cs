﻿using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IdeaStatiCa.Plugin.Grpc
{
	public interface IGrpcSender
	{
		Task SendMessageAsync(GrpcMessage message);
	}

	public interface IGrpcCommunicator : IGrpcSender
	{
		int Port { get; }

		string Host { get; }

		bool IsConnected { get; }

		void RegisterHandler(string handlerId, IGrpcMessageHandler handler);

		void Connect(string clientId, int port);

		/// <summary>
		/// Starts gRPC communication
		/// </summary>
		/// <param name="clientId">Current client ID (PID)</param>
		/// <param name="port">Port on which the server is running.</param>
		/// <returns></returns>
		Task StartAsync();

		/// <summary>
		/// Stopos gRPC communication
		/// </summary>
		/// <returns></returns>
		Task DisconnectAsync();
	}

	/// <summary>
	/// Client implementation for the Grpc connection.
	/// </summary>
	public class GrpcClient : IGrpcCommunicator
	{
		#region Private Fields
		private Channel channel;
		private Dictionary<string, IGrpcMessageHandler> handlers = new Dictionary<string, IGrpcMessageHandler>();
		private AsyncDuplexStreamingCall<GrpcMessage, GrpcMessage> client;
		private string errorMessage;
		protected readonly IPluginLogger Logger;
		private List<ChannelOption> channelOptions = new List<ChannelOption>()
				{
						new ChannelOption(ChannelOptions.MaxReceiveMessageLength, Constants.GRPC_MAX_MSG_SIZE),
						new ChannelOption(ChannelOptions.MaxSendMessageLength, Constants.GRPC_MAX_MSG_SIZE)
				};
		#endregion

		#region Properties & Events

		/// <summary>
		/// Triggered every time client connects. Sends a client ID in args.
		/// </summary>
		public event EventHandler ClientConnected;

		/// <summary>
		/// Triggered when client has disconnected. Sends a client ID in args.
		/// </summary>
		public event EventHandler ClientDisconnected;

		/// <summary>
		/// ID of the client. Typically the PID of the plugins process.
		/// </summary>
		public string ClientID { get; private set; }

		/// <summary>
		/// Port on which the server will communicate.
		/// </summary>
		public int Port { get; private set; }

		public string Host { get; private set; }

		/// <summary>
		/// Determines whether client is connected.
		/// </summary>
		public bool IsConnected { get; private set; }
		#endregion

		#region ctor
		/// <summary>
		/// Initializes the IdeaStatiCa Grpc client.
		/// </summary>
		/// <param name="logger">The instance of the logger</param>
		public GrpcClient(IPluginLogger logger)
		{
			Debug.Assert(logger != null);
			this.Logger = logger;
			Host = "localhost";
		}
		#endregion

		#region Methods
		/// <summary>
		/// Registers a message handler.
		/// </summary>
		/// <param name="handlerId">UniqueID of the handler.</param>
		/// <param name="handler">Handler implementation.</param>
		public void RegisterHandler(string handlerId, IGrpcMessageHandler handler)
		{
			if (handlers.ContainsKey(handlerId))
			{
				throw new ArgumentException($"Handler with ID {handlerId} is already registered.");
			}

			Logger.LogDebug($"GrpcClient.RegisterHandler handlerId = {handlerId}, handler = {handler.GetType().Name}");

			handlers.Add(handlerId, handler);
		}

		/// <summary>
		/// Connects to the server.
		/// </summary>
		/// <param name="clientId">Current client ID (PID)</param>
		/// <param name="port">Port on which the server is running.</param>
		/// <returns></returns>
		public void Connect(string clientId, int port)
		{
			ClientID = clientId;
			Port = port;

			string address = $"localhost:{Port}";
			Logger.LogDebug($"GrpcClient.ConnectAsync address = '{address}'");

			channel = new Channel(address, ChannelCredentials.Insecure, channelOptions);

			var serviceClient = new GrpcService.GrpcServiceClient(channel);

			client = serviceClient.ConnectAsync();
			IsConnected = true;

			ClientConnected?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Connects to the server.
		/// </summary>
		/// <returns></returns>
		public Task StartAsync()
		{
			return Task.Run(async () =>
		   {
			   string address = $"{Host}:{Port}";
			   Logger.LogDebug($"GrpcClient.ConnectAsync address = '{address}'");
			   try
			   {
					while (await ResponseStream())
				   {
					   var grpcMessage = client.ResponseStream.Current;
					   RunMessageHandler(grpcMessage);
				   }
			   }
			   catch (Exception e)
			   {
				   errorMessage = e.Message;
				   await DisconnectAsync();
			   }
		   });
		}

		private void RunMessageHandler(GrpcMessage message)
		{
			Task.Factory.StartNew(() =>
			{
				HandleMessageAsync(message);
			});
		}

		private async Task<bool> ResponseStream()
		{
			return await client.ResponseStream.MoveNext(cancellationToken: CancellationToken.None);
		}

		/// <summary>
		/// Disconnets client from the server.
		/// </summary>
		/// <returns></returns>
		public async Task DisconnectAsync()
		{
			Logger.LogDebug("GrpcClient.DisconnectAsync");
			try
			{
				await client?.RequestStream?.CompleteAsync();
				await channel.ShutdownAsync();
			}
			catch(Exception e)
			{
				Logger.LogWarning("GprcClient.DisconnectAsync", e);
			}
			finally
			{
				IsConnected = false;
				ClientDisconnected?.Invoke(this, EventArgs.Empty);
			}
		}

		/// <summary>
		/// Sends a message to the server.
		/// </summary>
		/// <param name="message">Grpc message to send.</param>
		/// <param name="data">Body of the message.</param>
		/// <returns></returns>
		public Task SendMessageAsync(GrpcMessage message)
		{
			Logger.LogDebug($"GrpcClient.GrpcClient MessageName = ${message?.MessageName}, ClientId = ${message?.ClientId}, OperationId = ${message?.OperationId}");
			if (IsConnected)
			{
				return client.RequestStream.WriteAsync(message);
			}
			else
			{
				throw new Exception("Client disconnected.");
			}
		}

		/// <summary>
		/// Handle incoming messages.
		/// </summary>
		/// <param name="message">Message incoming from server.</param>
		/// <returns></returns>
		internal virtual void HandleMessageAsync(GrpcMessage message)
		{
			Logger.LogDebug($"GrpcClient.HandleMessageAsync MessageName = ${message?.MessageName}, ClientId = ${message?.ClientId}, OperationId = ${message?.OperationId}");
			var handler = handlers.ContainsKey(message.MessageName) ? handlers[message.MessageName] : null;

			if (handler != null)
			{
				Logger.LogDebug($"GrpcClient.HandleMessageAsync calling handler Handler = {handler.GetType().Name}, MessageName = {message?.MessageName},MessageType = '{message?.MessageType}'");

				if (message?.MessageType == GrpcMessage.Types.MessageType.Request)
				{
					handler.HandleServerMessage(message, this);
				}
				else
				{
					handler.HandleClientMessage(message, this);
				}
			}
			else
			{
				throw new ApplicationException($"Grpc reflection error. Message handler '{message.MessageName}' is not registered!");
			}
		}
		#endregion
	}
}
