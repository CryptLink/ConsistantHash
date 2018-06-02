﻿using CryptLink.ConsistentHash;
using CryptLink.SigningFramework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrypthLink
{
    //based on consistent-hash https://code.google.com/p/consistent-hash/  (GNU Lesser GPL)

    /// <summary>
    /// A distributed hash table designed to find hosts in a peer-to-peer swarm
    /// </summary>
    /// <typeparam name="T">Type of object to store, must be an abstract class of Hashable</typeparam>
    public class ConsistentHash<T> where T : IHashable {

        SortedDictionary<Hash, T> circle = new SortedDictionary<Hash, T>();
        SortedDictionary<Hash, T> unreplicatedNodes = new SortedDictionary<Hash, T>();
        Dictionary<Hash, int> replicationWeights = new Dictionary<Hash, int>();
        HashProvider Provider;

        Hash[] allKeys = null;    //cache the ordered keys for better performance

        public ConsistentHash(HashProvider _Provider) {
            Provider = _Provider;
        }

        /// <summary>
        /// Gets a list of all nodes, this is inefficient and should not be used unless you need one instance of every node for enumeration
        /// </summary>
        public List<T> AllNodes {
            get {
                return unreplicatedNodes.Values.ToList();
            }
        }

        /// <summary>
        /// Gets the count of all nodes, each node is 1 count regardless of replication weight
        /// </summary>
        public long NodeCount {
            get {
                return unreplicatedNodes.Values.Count();
            }
        }

        /// <summary>
        /// Adds a node, runs replication
        /// </summary>
        /// <param name="node">Item to store, must be abstracted from Hashable</param>
        /// <param name="updateKeyArray">If enabled updates the key cache</param>
        /// <param name="ReplicationWeight">Nodes are added multiple times to the namespace to allow for better 
        /// distribution of peers in the address space, the higher the weight the more likely the node will be found while searching</param>
        /// <returns>The first hash of the node (If ReplicationWeight is more than 1, there is more than one hash of the item in the table)</returns>
        public Hash Add(T node, bool updateKeyArray, int ReplicationWeight) {
            var nodeHash = node.ComputedHash;
            Add(node, nodeHash, updateKeyArray, ReplicationWeight);
            return nodeHash;
        }

        public void AddRange(List<T> nodes, bool updateKeyArray, int ReplicationWeight) {
            if (nodes == null) {
                return;
            }

            foreach (var node in nodes) {
                Add(node, node.ComputedHash, false, ReplicationWeight);
            }

            if (updateKeyArray) {
                UpdateKeyArray();
            }
        }

        /// <summary>
        /// Adds a node, runs replication
        /// </summary>
        /// <param name="node">Item to store</param>
        /// <param name="nodeHash">Hash of the item. If the node inherits from Hashable, use the overload that does not require this</param>
        /// <param name="updateKeyArray">If enabled updates the key cache</param>
        /// <param name="ReplicationWeight">Nodes are added multiple times to the namespace to allow for better 
        /// distribution of peers in the address space, the higher the weight the more likely the node will be found while searching</param>
        public void Add(T node, Hash nodeHash, bool updateKeyArray, int ReplicationWeight) {
            circle[nodeHash] = node;
            var rehashed = nodeHash;

            for (int i = 0; i < ReplicationWeight; i++) {
                rehashed = rehashed.Rehash();
                circle[rehashed] = node;
            }

            if (unreplicatedNodes.ContainsKey(nodeHash) == false) {
                replicationWeights.Add(nodeHash, ReplicationWeight);
                unreplicatedNodes.Add(nodeHash, node);
            }

            if (updateKeyArray) {
                UpdateKeyArray();
            }
        }

        /// <summary>
        /// Updates the list of ordered keys for faster lookups
        /// </summary>
        public void UpdateKeyArray() {
            allKeys = circle.Keys.ToArray();
        }

        /// <summary>
        /// Removes node and all replicas
        /// </summary>
        /// <param name="node"></param>
        public void Remove(T node, Hash nodeHash) {
            int replicationWeight = replicationWeights[nodeHash];

            Hash newHash = nodeHash;

            for (int i = 0; i < replicationWeight; i++) {
                newHash = nodeHash.Rehash();

                if (!circle.Remove(newHash)) {
                    throw new Exception("Error removing replicated hashes, this should only happen if: " +
                    "1. There was a hash collision, 2. The key array was modified outside of this logic");
                }
            }

            unreplicatedNodes.Remove(nodeHash);

            allKeys = circle.Keys.ToArray();
        }

        /// <summary>
        /// return the index of first item that is greater than 'Value'
        /// </summary>
        /// <returns>if there are no nodes, or search fails, will return 0</returns>
        int FirstGreater(Hash Value) {
            int begin = 0;
            int end = allKeys.Length - 1;

            if (allKeys[end] < Value || allKeys[0] > Value) {
                return 0;
            }

            int mid = begin;
            while (end - begin > 1) {
                mid = (end + begin) / 2;
                if (allKeys[mid] >= Value) {
                    end = mid;
                } else {
                    begin = mid;
                }
            }

            if (allKeys[begin] > Value || allKeys[end] < Value) {
                throw new Exception("No nodes in the search space, this should not happen, there may be data corruption");
            }

            return end;
        }

        public T GetNode(byte[] key) {
            Hash h = Hash.FromComputedBytes(key, Provider, 0);
            int first = FirstGreater(h);
            return circle[allKeys[first]];
        }

        public T GetNode(Hash key) {
            int first = FirstGreater(key);
            return circle[allKeys[first]];
        }

        public bool ContainsNode(Hash key) {
            return allKeys.Contains(key);
        }

    }
}