namespace Aegis.DomainRules

open System

module ProtocolStateMachine =

    type HandshakeState =
        | Idle
        | AwaitingClientFinish of cookie: byte[] * expiresAtMs: int64
        | SessionEstablished of sessionId: uint64 * establishedAtMs: int64
        | Authenticated of sessionId: uint64 * userId: uint64
        | Closed of reason: string

    type HandshakeEvent =
        | ReceiveClientHello of nowMs: int64 * clientTimeMs: int64 * cookie: byte[] * ttlMs: int64
        | ReceiveClientFinish of nowMs: int64 * cookie: byte[]
        | MarkAuthenticated of userId: uint64
        | ForceClose of reason: string

    let private withinSkew (serverNowMs: int64) (clientTimeMs: int64) =
        let diff = Math.Abs(serverNowMs - clientTimeMs)
        diff <= 90_000L

    let apply (state: HandshakeState) (event: HandshakeEvent) : Result<HandshakeState, string> =
        match state, event with
        | Closed _, _ ->
            Error "Connection already closed"

        | _, ForceClose reason ->
            Ok (Closed reason)

        | Idle, ReceiveClientHello(nowMs, clientTimeMs, cookie, ttlMs) ->
            if isNull cookie || cookie.Length = 0 then
                Error "Missing anti-DoS cookie"
            elif ttlMs <= 0L || ttlMs > 120_000L then
                Error "Invalid cookie TTL"
            elif not (withinSkew nowMs clientTimeMs) then
                Error "Client clock skew is outside allowed range"
            else
                Ok (AwaitingClientFinish(cookie, nowMs + ttlMs))

        | AwaitingClientFinish(expectedCookie, expiresAtMs), ReceiveClientFinish(nowMs, cookie) ->
            if nowMs > expiresAtMs then
                Error "Handshake cookie expired"
            elif isNull cookie || cookie.Length <> expectedCookie.Length then
                Error "Invalid cookie"
            elif not (Array.forall2 (=) cookie expectedCookie) then
                Error "Cookie mismatch"
            else
                let sessionId = uint64 nowMs
                Ok (SessionEstablished(sessionId, nowMs))

        | SessionEstablished(sessionId, _), MarkAuthenticated userId when userId > 0UL ->
            Ok (Authenticated(sessionId, userId))

        | AwaitingClientFinish _, MarkAuthenticated _ ->
            Error "Cannot authenticate before handshake completion"

        | Idle, MarkAuthenticated _ ->
            Error "Handshake not started"

        | _, _ ->
            Error "Invalid state transition"

    // Small probe entry point for fuzz/property tests in C#.
    let fuzzTransition (stateCode: int) (eventCode: int) (nowMs: int64) (clientTimeMs: int64) (userId: uint64) : bool =
        let state =
            match stateCode with
            | 0 -> Idle
            | 1 -> AwaitingClientFinish([| 1uy; 2uy; 3uy |], nowMs + 30_000L)
            | 2 -> SessionEstablished(uint64 nowMs, nowMs)
            | 3 -> Authenticated(uint64 nowMs, if userId = 0UL then 1UL else userId)
            | _ -> Closed("fuzz")

        let event =
            match eventCode with
            | 0 -> ReceiveClientHello(nowMs, clientTimeMs, [| 1uy; 2uy; 3uy |], 30_000L)
            | 1 -> ReceiveClientFinish(nowMs, [| 1uy; 2uy; 3uy |])
            | 2 -> MarkAuthenticated(if userId = 0UL then 1UL else userId)
            | _ -> ForceClose("fuzz")

        apply state event |> Result.isOk
