# 实时连接说明

本目录实现游戏的 gRPC/HTTP2 实时服务，包括公共频道、社团频道和协力实时大厅。

## 必须使用 HTTPS

原版 Android 客户端不会稳定地连接明文 `http://` 实时地址。即使本地服务端使用明文 HTTP/2 正常监听 `8788`，客户端也可能在 `CircleRealtimeClientHelper.JoinAsync` 或 `CommonRealtimeClientHelper.JoinAsync` 等待处停住，服务端看不到实时连接。

因此，`Environment` 接口返回的第 12 项实时地址必须是 HTTPS，例如：

```text
https://realtime.example.com
```

HTTP API 可以继续使用局域网 HTTP 地址，例如：

```text
http://192.168.10.25:8787
```

不要通过跳过 `JoinAsync` 来掩盖问题。那会让主页继续加载，但会关闭社团和公共实时功能。

## 本地测试方式

最低风险的测试方式是使用 Cloudflare Quick Tunnel，将本地明文 HTTP/2 实时端口转发为受信任的公网 HTTPS 地址：

```powershell
cloudflared tunnel --url http://192.168.10.25:8788 --no-autoupdate
```

命令启动后会输出临时地址：

```text
https://<随机名称>.trycloudflare.com
```

将该地址写入服务端 `.env`：

```dotenv
REALTIME_BASE_URL=https://<随机名称>.trycloudflare.com
```

然后重新启动服务端，使 `Environment` 返回新的实时地址。Cloudflare Tunnel 进程必须持续运行；停止进程后临时地址立即失效。

## 验证顺序

1. 确认服务端监听 `8788`：

   ```powershell
   Get-NetTCPConnection -State Listen -LocalPort 8788
   ```

2. 启动 Cloudflare Tunnel，并确认日志出现 `Registered tunnel connection`。
3. 重启服务端。
4. 检查 `requests.log` 中的 `Environment` 响应，确认实时地址以 `https://` 开头。
5. 启动客户端，确认主页初始化能够继续完成。
6. 用 `Get-NetTCPConnection` 或服务端日志确认实时连接确实到达，而不是只验证 HTTP API。
