﻿using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using AElf.Configuration.Config.Chain;
using AElf.Cryptography.ECDSA;
using AElf.Network.Connection;
using AElf.Network.Data;
using AElf.Network.Peers;
using Google.Protobuf;
using Moq;
using Xunit;

namespace AElf.Network.Tests
{
    public class PeerTests
    {
        [Fact]
        public void Peer_InitialState()
        {
            Peer p = new Peer(new TcpClient(), null, null, 1234, null, null);
            Assert.False(p.IsAuthentified);
            Assert.False(p.IsDisposed);
        }

        [Fact]
        public void Start_Disposed_ThrowsException()
        {
            Peer p = new Peer(new TcpClient(), null, null, 1234, null, null);
            p.Dispose();

            Assert.Throws<ObjectDisposedException>(() => p.Start());
        }
        
        [Fact]
        public void Start_Disposed_ThrowsInvalidOperationException()
        {
            ChainConfig.Instance.ChainId = "kPBx";
            
            int port = 1234;
            
            Peer p = new Peer(new TcpClient(), null, null, port, null, null);
            
            var (_, handshake) = NetworkTestHelpers.CreateKeyPairAndHandshake(port);
            
            RejectReason reason = p.AuthentifyWith(handshake);
            Assert.Equal(RejectReason.None, reason);

            var ex = Assert.Throws<InvalidOperationException>(() => p.Start());
            Assert.Equal("Cannot start an already authentified peer.", ex.Message);
        }
        
        [Fact(Skip = "Being refactored")]
        public void Start_ShouldSend_Auth()
        {
            ChainConfig.Instance.ChainId = "kPBx";
            
            int peerPort = 1234;
            
            Mock<IMessageReader> reader = new Mock<IMessageReader>();
            Mock<IMessageWriter> messageWritter = new Mock<IMessageWriter>();

            ECKeyPair kp = new KeyPairGenerator().Generate();
            Peer p = new Peer(new TcpClient(), reader.Object, messageWritter.Object, peerPort, kp, null);

            Message authMessage = null;
            messageWritter.Setup(w => w.EnqueueMessage(It.IsAny<Message>(), It.IsAny<Action<Message>>())).Callback<Message, Action<Message>>((m, a) => authMessage = m);
            
            p.Start();
            
            Assert.NotNull(authMessage);
            Assert.Equal(0, authMessage.Type);

            Handshake handshake = Handshake.Parser.ParseFrom(authMessage.Payload);
            
            Assert.Equal(peerPort, handshake.NodeInfo.Port);
        }

        [Fact(Skip = "Being refactored")]
        public void Start_AuthentificationTimout_ShouldThrowEvent()
        {
            ChainConfig.Instance.ChainId = "kPBx";
            
            Mock<IMessageReader> reader = new Mock<IMessageReader>();
            Mock<IMessageWriter> messageWritter = new Mock<IMessageWriter>();
            
            ECKeyPair key = new KeyPairGenerator().Generate();
            Peer p = new Peer(new TcpClient(), reader.Object, messageWritter.Object, 1234, key, null);
            p.AuthTimeout = 100;

            AuthFinishedArgs authFinishedArgs = null;
            
            p.AuthFinished += (sender, args) =>
            {
                authFinishedArgs = args as AuthFinishedArgs;
            };
            
            p.Start();
            
            Task.Delay(200).Wait();
            
            Assert.NotNull(authFinishedArgs);
            Assert.False(authFinishedArgs.IsAuthentified);
            Assert.True(authFinishedArgs.Reason == RejectReason.AuthTimeout);
            Assert.False(p.IsAuthentified);
        }
        
        [Fact]
        public void Start_AuthentificationNoTimout_ShouldThrowEvent()
        {
            ChainConfig.Instance.ChainId = "kPBx";
                
            int localPort = 1234;
            int remotePort = 1235;
            
            var (_, handshake) = NetworkTestHelpers.CreateKeyPairAndHandshake(remotePort);
            
            Mock<IMessageReader> reader = new Mock<IMessageReader>();
            Mock<IMessageWriter> messageWritter = new Mock<IMessageWriter>();
            
            ECKeyPair key = new KeyPairGenerator().Generate();
            Peer p = new Peer(new TcpClient(), reader.Object, messageWritter.Object, localPort, key, null);
            p.AuthTimeout = 10000;
            
            AuthFinishedArgs authFinishedArgs = null;
            
            p.AuthFinished += (sender, args) => {
                authFinishedArgs = args as AuthFinishedArgs;
            };

            // if (handshake.PublicKey == null || handshake.PublicKey.Length < 0)
            Assert.True(handshake.PublicKey != null);
            Assert.True(handshake.PublicKey.Length >= 0);

            p.Start();
            p.AuthentifyWith(handshake);
            
            Task.Delay(200).Wait();
            
            Assert.Null(authFinishedArgs);
            Assert.True(p.IsAuthentified);
        }
    }
}