﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Tests;
using UnityEngine;
using Random = UnityEngine.Random;

public class CubeClient : MonoBehaviour
{
    public int sendPort;
    public int recvPort;
    public Channel sendChannel;
    public Channel recvChannel;

    public int userID;
    public int displaySeq = 0;
    public float time = 0;
    public bool isPlaying;
    public PlayerJoined playersToInstantiate = new PlayerJoined();

    private List<Snapshot> interpolationBuffer = new List<Snapshot>();
    private List<Commands> commands = new List<Commands>();
    private Dictionary<int, GameObject> cubes = new Dictionary<int, GameObject>();

    public GameObject cubePrefab;

    public Color clientColor;
    
    public int interpolationCount = 2;

    public bool ISFIRST;

    public void Initialize(int sendPort, int recvPort, int userID)
    {
        this.sendPort = sendPort;
        this.sendChannel = new Channel(sendPort);
        this.recvPort = recvPort;
        this.recvChannel = new Channel(recvPort);
        this.userID = userID;
        clientColor = new Color(Random.value, Random.value, Random.value);
    }

    private void Update()
    {
        var packet = recvChannel.GetPacket();

        if (packet != null)
        {
            var buffer = packet.buffer;
            
            //deserialize
            CubeEntity.ClientDeserialize(interpolationBuffer, playersToInstantiate, buffer, displaySeq, commands, ISFIRST);
            if (!ISFIRST)
            {
                // Debug.Log($"USER {userID} Buffer {interpolationBuffer.Count}");
                // Debug.Log($"Display Seq {displaySeq}");
                // Debug.Log($"Buffer Seq {interpolationBuffer[0].Seq}");
            }
                
            //networkSeq++;
        }

        ReadClientInput();

        if (interpolationBuffer.Count >= interpolationCount)
            isPlaying = true;
        else if (interpolationBuffer.Count <= 1)
            isPlaying = false;
        
        if (isPlaying)
        {
            //accumCli += Time.deltaTime;
            time += Time.deltaTime;

            if (playersToInstantiate.InstantiateCubesPending)
            {
                InstantiateCubes(playersToInstantiate);
                playersToInstantiate.InstantiateCubesPending = false;
                displaySeq = interpolationBuffer[0].Seq;
                time = interpolationBuffer[0].Time;
            }

            var previousTime = interpolationBuffer[0].Time;
            var nextTime = interpolationBuffer[1].Time;
            if (time >= nextTime) {
                interpolationBuffer.RemoveAt(0);
                displaySeq++;
                if (interpolationBuffer.Count < 2)
                {
                    isPlaying = false;
                    return;
                }
                previousTime = interpolationBuffer[0].Time;
                nextTime =  interpolationBuffer[1].Time;
            }
            var t =  (time - previousTime) / (nextTime - previousTime);
            Interpolate(interpolationBuffer[0], interpolationBuffer[1], t);
        }
    }
    
    private void ReadClientInput()
    {
        Commands currentCommands = new Commands(
            userID,
            Input.GetKeyDown(KeyCode.UpArrow),
            Input.GetKeyDown(KeyCode.DownArrow),
            Input.GetKeyDown(KeyCode.RightArrow),
            Input.GetKeyDown(KeyCode.LeftArrow),
            Input.GetKeyDown(KeyCode.Space)
        );
        
        if (currentCommands.hasCommand())
        {
            commands.Add(currentCommands);
            //serialize
            var packet = Packet.Obtain();
            CubeEntity.ClientSerializeInput(commands, packet.buffer);
            packet.buffer.Flush();

            string serverIP = "127.0.0.1";
            var remoteEp = new IPEndPoint(IPAddress.Parse(serverIP), sendPort);
            sendChannel.Send(packet, remoteEp);

            packet.Free();
        }
    }

    private void InstantiateCubes(PlayerJoined playerJoined)
    {
        if (cubes.Count == 0) // this client is the player who just joined
        {
            foreach (var userStatePair in interpolationBuffer[0].UserStates)
            {
                var player = Instantiate(cubePrefab, transform);
                Renderer rndr = player.GetComponent<Renderer>();
                rndr.material.color = clientColor;
                // player.GetComponent<>()
                cubes.Add(userStatePair.Key, player);
            }
        }
        else // just instantiate the new player
        {
            var newPlayer = Instantiate(cubePrefab, transform);
            var rndr = newPlayer.GetComponent<Renderer>();
            rndr.material.color = clientColor;
            cubes.Add(playerJoined.UserID, newPlayer);
        }
    }
    
    private void Interpolate(Snapshot prevSnapshot, Snapshot nextSnapshot, float t)
    {
        if (time.Equals(playersToInstantiate.Time))
        {
            //Debug.Log($"MOSTRANDO STATES DEL USER {userID}");
            foreach (var state in prevSnapshot.UserStates)
            {
                //Debug.Log($"User: {state.Key} - {state}");
            }
        }

        //Debug.Log(prevSnapshot + " " + nextSnapshot);
        foreach (var userCubePair in cubes)
        {
            if (!prevSnapshot.UserStates.ContainsKey(userCubePair.Key))
                continue;
            
            var position = new Vector3();
            var rotation = new Quaternion();

            UserState prevUserState = prevSnapshot.UserStates[userCubePair.Key];
            UserState nextUserState = nextSnapshot.UserStates[userCubePair.Key];
            
            position.x = InterpolateAxis(prevUserState.Position.x, nextUserState.Position.x, t);
            position.y = InterpolateAxis(prevUserState.Position.y, nextUserState.Position.y, t);
            position.z = InterpolateAxis(prevUserState.Position.z, nextUserState.Position.z, t);
    
            rotation.w = InterpolateAxis(prevUserState.Rotation.w, nextUserState.Rotation.w, t);
            rotation.x = InterpolateAxis(prevUserState.Rotation.x, nextUserState.Rotation.x, t);
            rotation.y = InterpolateAxis(prevUserState.Rotation.y, nextUserState.Rotation.y, t);
            rotation.z = InterpolateAxis(prevUserState.Rotation.z, nextUserState.Rotation.z, t);
    
            userCubePair.Value.transform.position = position;
            userCubePair.Value.transform.rotation = rotation;
        }
    }

    private float InterpolateAxis(float currentSnapValue, float nextSnapValue, float t)
    {
        return currentSnapValue + (nextSnapValue - currentSnapValue) * t;
    }
    
    private void OnDestroy() {
        sendChannel.Disconnect();
        recvChannel.Disconnect();
    }
}