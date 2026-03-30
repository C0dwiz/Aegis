# Aegis Protocol Docs

Документация протокола разделена на два уровня:

- `protocol-overview.md` - короткий обзор архитектуры, жизненного цикла и security-модели.
- `wire-spec.md` - полный wire-level документ: поля, флаги, коды, payload-контракты и примеры.

## Быстрые ссылки

- Обзор: `protocol-overview.md`
- Спецификация: `wire-spec.md`
- Enum типов сообщений: `src/Aegis.Protocol/MessageType.cs`
- Энкодер/декодер: `src/Aegis.Protocol/MessageEncoder.cs`

## Генерация таблицы MessageType

```bash
python tools/generate_message_type_table.py
```

## Важное замечание по совместимости

- Wire header фиксирован: `Magic(4) + VerMaj(1) + VerMin(1) + Flags(1) + Type(2) + SequenceId(8) + PayloadLength(4)`.
- Для handshake signature transcript длины ключей кодируются в little-endian int32.
