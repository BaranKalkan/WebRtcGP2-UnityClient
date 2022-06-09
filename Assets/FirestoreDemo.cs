using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Firestore;
using Unity.WebRTC;
using UnityEngine;
using Newtonsoft.Json;
using TMPro;
using Unity.VisualScripting;
using UnityEngine.UI;
using ColorUtility = UnityEngine.ColorUtility;

public class FirestoreDemo : MonoBehaviour
{
    public static FirestoreDemo instance;
    
    [SerializeField] private TMP_InputField InputField;
    
    [SerializeField] private MeshRenderer targetMaterial;
    [SerializeField] private GameObject Lights;
    
    private MediaStream videoStream;
    [SerializeField] private Camera cam;
    internal static class WebRTCSettings
    {
        public const int DefaultStreamWidth = 1280;
        public const int DefaultStreamHeight = 720;

        private static bool s_limitTextureSize = true;
        private static Vector2Int s_StreamSize = new Vector2Int(DefaultStreamWidth, DefaultStreamHeight);
        private static RTCRtpCodecCapability s_useVideoCodec = null;

        public static bool LimitTextureSize
        {
            get { return s_limitTextureSize; }
            set { s_limitTextureSize = value; }
        }

        public static Vector2Int StreamSize
        {
            get { return s_StreamSize; }
            set { s_StreamSize = value; }
        }

        public static RTCRtpCodecCapability UseVideoCodec
        {
            get { return s_useVideoCodec; }
            set { s_useVideoCodec = value; }
        }
    }
    
    private void Awake()
    {
        WebRTC.Initialize();
        
        videoStream = cam.CaptureStream(WebRTCSettings.StreamSize.x, WebRTCSettings.StreamSize.y, 10000);
        
        instance = this;
    }
    private bool videoUpdateStarted = false;
    private void AddTracks()
    {
        foreach (var track in videoStream.GetTracks())
        {
            peerConnection.AddTrack(track, videoStream);
        }

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }
    
    public void StartProgress()
    {
        StartCoroutine(ConnectRoom());
    }
    private static RTCConfiguration GetConfiguration()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] {new RTCIceServer {urls = new[] {"stun:stun1.l.google.com:19302","stun:stun2.l.google.com:19302"}}};
        config.iceCandidatePoolSize = 10;
        return config;
    }

    private RTCPeerConnection peerConnection;
    public IEnumerator ConnectRoom()
    {
        
        Debug.Log("Trying to connect!");
        var roomId = InputField.text;
        
        var instance = FirebaseFirestore.DefaultInstance;
        var roomRef = instance.Collection("rooms").Document(roomId);
        
        roomRef.GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            var roomSnapshot = task.Result;
            
            Debug.Log("Snapshot:" + roomSnapshot);
            
            if (!roomSnapshot.Exists) return;
            
            var configuration = GetConfiguration();
            Debug.Log("Create PeerConnection with configuration: "+ configuration);
                
            peerConnection = new RTCPeerConnection(ref configuration);
            
            AddTracks();
            
            peerConnection.OnIceConnectionChange += IceConnectionChange;
            peerConnection.OnConnectionStateChange += ConnectionStateChange;
            peerConnection.OnDataChannel += ReceiveChannelCallback;

            // Code for collecting ICE candidates below
            var calleeCandidatesCollection = roomRef.Collection("calleeCandidates");
            peerConnection.OnIceCandidate += candidate =>
            {
                if (candidate == null)
                {
                    Debug.Log("Got final candidate!");
                    return;
                }

                Debug.Log("Got candidate:" + candidate);
                var data = new Dictionary<string, string>()
                {
                    { "candidate", candidate.Candidate },
                    { "sdpMLineIndex", candidate.SdpMLineIndex.ToString() },
                    { "sdpMid", candidate.SdpMid },
                };
                
                calleeCandidatesCollection.AddAsync(data).ContinueWithOnMainThread(task1 => {
                    DocumentReference addedDocRef = task1.Result;
                    Debug.Log($"Added document with ID: {addedDocRef.Id}.");
                });
            };
                
            Debug.Log("Getting offer!");
            var offer = roomSnapshot.GetValue<Dictionary<string,string>>("offer");
            Debug.Log("Got offer!" + offer["sdp"]);

            StartCoroutine(Answer(offer, peerConnection, roomRef));
       
        });
        yield return null;
    }

    private void ConnectionStateChange(RTCPeerConnectionState state)
    {
        switch (state)
        {
            case RTCPeerConnectionState.New:
                Debug.Log("RTCPeerConnectionState.New");
                break;
            case RTCPeerConnectionState.Connecting:
                Debug.Log("RTCPeerConnectionState.Connecting");
                break;
            case RTCPeerConnectionState.Connected:
                Debug.Log("RTCPeerConnectionState.Connected");
                break;
            case RTCPeerConnectionState.Disconnected:
                Debug.Log("RTCPeerConnectionState.Disconnected");
                break;
            case RTCPeerConnectionState.Failed:
                Debug.Log("RTCPeerConnectionState.Failed");
                break;
            case RTCPeerConnectionState.Closed:
                Debug.Log("RTCPeerConnectionState.Closed");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private void IceConnectionChange(RTCIceConnectionState state)
    {
        switch (state)
        {
            case RTCIceConnectionState.New:
                Debug.Log("RTCIceConnectionState.New");
                break;
            case RTCIceConnectionState.Checking:
                Debug.Log("RTCIceConnectionState.Checking");
                break;
            case RTCIceConnectionState.Connected:
                Debug.Log("RTCIceConnectionState.Connected");
                break;
            case RTCIceConnectionState.Completed:
                Debug.Log("RTCIceConnectionState.Completed");
                break;
            case RTCIceConnectionState.Failed:
                Debug.Log("RTCIceConnectionState.Failed");
                break;
            case RTCIceConnectionState.Disconnected:
                Debug.Log("RTCIceConnectionState.Disconnected");
                break;
            case RTCIceConnectionState.Closed:
                Debug.Log("RTCIceConnectionState.Closed");
                break;
            case RTCIceConnectionState.Max:
                Debug.Log("RTCIceConnectionState.Max");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private IEnumerator Answer(Dictionary<string, string> offer, RTCPeerConnection peerConnection, DocumentReference roomRef)
    {
        var offerDesc = new RTCSessionDescription
        {
            sdp = offer["sdp"],
            type = offer["type"].Equals("offer") ? RTCSdpType.Offer : RTCSdpType.Answer
        };

        Debug.Log("setting remote desc");
        var remoteDescription = peerConnection.SetRemoteDescription(ref offerDesc);
        yield return remoteDescription;
        
        Debug.Log("creating answer");
        var rtcSessionDescriptionAsyncOperation = peerConnection.CreateAnswer();
        yield return rtcSessionDescriptionAsyncOperation;
        
        Debug.Log("setting local desc");
        var answerDesc = rtcSessionDescriptionAsyncOperation.Desc;
        
        var localDescription = peerConnection.SetLocalDescription(ref answerDesc);
        yield return localDescription;

        var roomWithAnswer = new Dictionary<string, object>
        {
            {
                "answer", new Dictionary<string, object>
                {
                    { "type", "answer" },
                    { "sdp", answerDesc.sdp }
                }
            }
        };
        
        Debug.Log("answer uploading");
        roomRef.SetAsync(roomWithAnswer, SetOptions.MergeAll).ContinueWithOnMainThread(t =>
        {
            Debug.Log("answer uploaded");
            
            
            roomRef.Collection("callerCandidates").Listen(snapshot =>
            {
                Debug.Log("callerCandidates change detected");
                foreach (var change in snapshot.GetChanges())
                {
                    Debug.Log("checking change type");
                    if (change.ChangeType == DocumentChange.Type.Added)
                    {
                        Debug.Log("add type change detected");
                        var test = change.Document.ToDictionary();
                       
                        Debug.Log("creating icecandidateinit");
                        var candidateInit = new RTCIceCandidateInit()
                        {
                            candidate = test["candidate"].ToString(),
                            sdpMid = test["sdpMid"].ToString(),
                            sdpMLineIndex = Convert.ToInt32(test["sdpMLineIndex"]),
                        };
                        
                        Debug.Log("adding icecandidate to peerconnection with ---> " +  test["candidate"]);
                        var rtcIceCandidate = new RTCIceCandidate(candidateInit);
                        peerConnection.AddIceCandidate(rtcIceCandidate);
                        peerConnection.OnIceCandidate.Invoke(rtcIceCandidate);
                        Debug.Log("added icecandidate");
                    }
                }
            });
        });
        
        
    }

    public RTCDataChannel messageChannel;
    private void ReceiveChannelCallback(RTCDataChannel channel)
    {
        messageChannel = channel;

        peerConnection.OnConnectionStateChange += state =>
        {
            if (state == RTCPeerConnectionState.Connected)
            {
                messageChannel.OnOpen += () => messageChannel.Send("test");
            }
        };
        
        messageChannel.OnMessage += bytes =>
        {
            var message = System.Text.Encoding.UTF8.GetString(bytes);
            if(message.StartsWith("ChangeColor:"))
            {
                var command = message[12..];
                if (ColorUtility.TryParseHtmlString(command, out var color))
                {
                    targetMaterial.material.color = color;
                }
                else
                {
                    Debug.Log("Invalid color code: " + message[12..]);
                }
            }
            else if(message.StartsWith("Lights:"))
            {
                var command = message[7..];
                switch (command)
                {
                    case "on":
                        Lights.SetActive(true);
                        break;
                    case "off":
                        Lights.SetActive(false);
                        break;
                    default:
                        Debug.Log("Invalid light mode: " + command);
                        break;
                }
            }
            else
            {
                Debug.Log(message);
            }
        };
        
    }

}
