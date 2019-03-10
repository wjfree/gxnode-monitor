﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GxchainCsharpSdk;

namespace gxnode_monitor
{
    class GxNodeMonitor
    {
        static private MonitorConfig config;
        static GxChainApi gxChainApi;
        static string accountId;
        const int interval = 60;

        static void Main(string[] args)
        {
            Console.WriteLine("********** Start Monitor ***********");

            config = MonitorConfig.LoadFromConfig("config.json");

            gxChainApi = new GxChainApi(config.api_url);

            //get account id from name
            accountId = gxChainApi.GetAccountByName(config.witness_id).Result.id;

            //计算nodeinfos的长度
            nodeInfos = new List<NodeInfo>();
            nodeMaxLen = config.miss_block_interval / interval; //最长时间间隔除以每分钟，即每分钟存储一次

            do
            {
                Console.WriteLine("Checking node " + config.witness_id + " ......");
                var r = gxChainApi.GetWitnessByAccount(accountId).Result;

                //获取丢块数以及投票数量
                NodeInfo node = new NodeInfo
                {
                    total_missed = r.total_missed,
                    total_votes = ulong.Parse(r.total_votes)
                };

                SavaNodeInfo(node);

                CheckMissBlock(config.warn_miss_block_count, config.switch_miss_block_count);

                Console.WriteLine("当前丢块数：\t" + node.total_missed + "\t当前投票数量：\t" + node.total_votes);

                Console.WriteLine("Now, Sleep " + interval + "s......");
                Thread.Sleep(interval * 1000);

            } while (true);

        }

        private class NodeInfo
        {
            public int total_missed { get; set; }
            public ulong total_votes { get; set; }

            public override string ToString()
            {
                return total_missed + "," + total_votes;
            }
        }


        //用于报警判断
        static private List<NodeInfo> nodeInfos;
        static private int nodeMaxLen;

        static private void SavaNodeInfo(NodeInfo node)
        {
            nodeInfos.Add(node);
            if (nodeInfos.Count > nodeMaxLen)
            {
                nodeInfos.RemoveAt(0);
            }

            //同时写入文件
            using (var sw = File.AppendText("nodeRecode.csv"))
            {
                sw.WriteLine(node);
            }
        }

        static private void CheckMissBlock(int warnlimit, int switchlimit)
        {
            var nodeLast = nodeInfos[nodeInfos.Count - 1];
            var nodeFirst = nodeInfos[0];

            if (nodeLast.total_missed - nodeFirst.total_missed >= warnlimit &&
                nodeLast.total_missed - nodeFirst.total_missed < switchlimit)
            {
                Warning();
            }
            if (nodeLast.total_missed - nodeFirst.total_missed >= switchlimit)
            {
                bool rz = SwitchProduceKey();
                if (rz)
                {
                    //清除监控记录重新监控
                    nodeInfos.Clear();
                }
                else
                {
                    //do nothing. CheckMissBlock atcion will be triggered next minute.
                }
            }
        }


        static private void Warning()
        {
            Console.WriteLine("Warning！！！！！");

        }

        static private bool SwitchProduceKey()
        {
            //打开钱包
            //切换密钥
            Console.WriteLine("启动切换密钥过程，更换节点");

            var CurrentProduceKey = gxChainApi.GetWitnessByAccount(accountId).Result.signing_key;

            Console.WriteLine("当前出块密钥为：\t\t\t" + CurrentProduceKey);

            int i = config.produce_public_keys.IndexOf(CurrentProduceKey);

            if (i == 0)
            {
                i = 1;
            }
            else
            {
                i = 0;
            }


            CurrentProduceKey = config.produce_public_keys[i];

            Console.WriteLine("将要更换为：\t\t\t" + CurrentProduceKey);

            for (int ji = 0; ji < 10; ji++)
            {
                setProduceKey(CurrentProduceKey);

                if (CurrentProduceKey.Equals(gxChainApi.GetWitnessByAccount(accountId).Result.signing_key))
                {
                    Console.WriteLine("切换成功，当前签名密钥为：\t\t" + CurrentProduceKey);

                    return true;
                }
                Thread.Sleep(1000);
            }

            Console.WriteLine("切换失败，当前签名密钥为：\t\t" + gxChainApi.GetWitnessByAccount(accountId).Result.signing_key);

            return false;
        }

        private static void setProduceKey(string produceKey)
        {
            using (var process = new Process())
            {
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.FileName = config.cli_wallet_path;
                process.StartInfo.Arguments = "-s " + config.wss_url;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.WorkingDirectory = config.wallet_file_path;
                process.Start();
                using (var sw = process.StandardInput)
                {
                    sw.WriteLine("unlock " + config.wallet_passwd + "\r\n");
                    sw.WriteLine("update_witness " + config.witness_id + " null " + produceKey + " GXC true\r\n");
                    sw.WriteLine("\r\n");
                }
            }
        }
    }
}
