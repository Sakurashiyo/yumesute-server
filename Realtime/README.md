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