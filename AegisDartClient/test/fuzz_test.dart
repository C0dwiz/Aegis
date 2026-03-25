import 'dart:math';
import 'dart:typed_data';

import 'package:test/test.dart';
import 'package:aegis_client/aegis_client.dart';

/// Fuzz & round-trip tests for the Aegis protocol encoder/decoder.
///
/// These tests exercise edge cases and random inputs that unit tests
/// typically miss: malformed frames, boundary payload sizes, and
/// lossless encode → decode → encode round-trips.
void main() {
  final rng = Random(42); // Fixed seed for reproducibility.

  // ── Helpers ─────────────────────────────────────────────────────────

  Uint8List randomBytes(int length) {
    return Uint8List.fromList(
      List<int>.generate(length, (_) => rng.nextInt(256)),
    );
  }

  /// Build a valid message with a random payload of [payloadSize] bytes.
  Message validMessage({int payloadSize = 0, MessageType? type}) {
    final msg = Message.withType(
      type ?? MessageType.message,
      randomBytes(payloadSize),
    );
    msg.sequenceId = rng.nextInt(1 << 32);
    msg.flags = ProtocolConstants.flagRequiresAck;
    return msg;
  }

  // ── Fuzz: random garbage should never cause unhandled exceptions ────

  group('Fuzz: decode random garbage', () {
    for (var i = 0; i < 500; i++) {
      test('random frame #$i', () {
        final garbage = randomBytes(rng.nextInt(256));
        // Should throw ProtocolDecodeError (or ProtocolError), never crash.
        expect(
          () => MessageEncoder.decode(garbage),
          throwsA(isA<ProtocolError>()),
        );
      });
    }

    test('exactly header-sized garbage', () {
      final data = randomBytes(ProtocolConstants.headerSize);
      expect(
        () => MessageEncoder.decode(data),
        throwsA(isA<ProtocolError>()),
      );
    });

    test('header with valid magic but garbage rest', () {
      final data = randomBytes(ProtocolConstants.headerSize + 10);
      // Plant correct magic.
      final bd = ByteData.view(data.buffer);
      bd.setUint32(0, ProtocolConstants.magic, Endian.big);
      expect(
        () => MessageEncoder.decode(data),
        throwsA(isA<ProtocolError>()),
      );
    });
  });

  // ── Fuzz: malformed payload lengths ─────────────────────────────────

  group('Fuzz: malformed payload lengths', () {
    Uint8List buildFrameWithPayloadLength(int payloadLen) {
      final buf = Uint8List(ProtocolConstants.headerSize);
      final bd = ByteData.view(buf.buffer);
      bd.setUint32(0, ProtocolConstants.magic, Endian.big);
      buf[4] = ProtocolConstants.versionMajor;
      buf[5] = ProtocolConstants.versionMinor;
      buf[6] = 0; // flags
      bd.setUint16(7, MessageType.ping.value, Endian.big);
      bd.setUint64(9, 1, Endian.big); // seqId
      bd.setUint32(17, payloadLen, Endian.big);
      return buf;
    }

    test('payload length = maxPayloadSize + 1', () {
      final frame =
          buildFrameWithPayloadLength(ProtocolConstants.maxPayloadSize + 1);
      expect(
        () => MessageEncoder.decode(frame),
        throwsA(isA<ProtocolDecodeError>()),
      );
    });

    test('payload length = 0xFFFFFFFF', () {
      final frame = buildFrameWithPayloadLength(0xFFFFFFFF);
      expect(
        () => MessageEncoder.decode(frame),
        throwsA(isA<ProtocolDecodeError>()),
      );
    });

    test('payload length claims more than data', () {
      final frame = buildFrameWithPayloadLength(100);
      // Frame is only header-sized → mismatch.
      expect(
        () => MessageEncoder.decode(frame),
        throwsA(isA<ProtocolDecodeError>()),
      );
    });
  });

  // ── Round-trip: encode → decode → encode ────────────────────────────

  group('Round-trip: encode → decode → encode', () {
    test('empty payload', () {
      final original = validMessage(payloadSize: 0, type: MessageType.ping);
      final encoded1 = MessageEncoder.encode(original);
      final decoded = MessageEncoder.decode(encoded1);
      final encoded2 = MessageEncoder.encode(decoded);

      expect(encoded2, equals(encoded1));
    });

    test('small payload (below compression threshold)', () {
      final original = validMessage(payloadSize: 100);
      final encoded1 = MessageEncoder.encode(original);
      final decoded = MessageEncoder.decode(encoded1);

      expect(decoded.type, equals(original.type));
      expect(decoded.sequenceId, equals(original.sequenceId));
      expect(decoded.payload, equals(original.payload));

      final encoded2 = MessageEncoder.encode(decoded);
      expect(encoded2, equals(encoded1));
    });

    test('large payload (triggers compression)', () {
      final original = validMessage(payloadSize: 2048);
      final encoded = MessageEncoder.encode(original);
      final decoded = MessageEncoder.decode(encoded);

      // Payload content must survive compression round-trip.
      expect(decoded.payload, equals(original.payload));
      expect(decoded.type, equals(original.type));
      expect(decoded.sequenceId, equals(original.sequenceId));
    });

    for (var size in [1, 511, 512, 513, 1024, 4096, 65536]) {
      test('payload size = $size bytes', () {
        final original = validMessage(payloadSize: size);
        final encoded = MessageEncoder.encode(original);
        final decoded = MessageEncoder.decode(encoded);

        expect(decoded.magic, equals(ProtocolConstants.magic));
        expect(decoded.versionMajor, equals(ProtocolConstants.versionMajor));
        expect(decoded.type, equals(original.type));
        expect(decoded.sequenceId, equals(original.sequenceId));
        expect(decoded.payload, equals(original.payload));
      });
    }

    test('all message types round-trip', () {
      for (final type in MessageType.values) {
        if (type == MessageType.unknown) continue;
        final original = validMessage(payloadSize: 50, type: type);
        final encoded = MessageEncoder.encode(original);
        final decoded = MessageEncoder.decode(encoded);
        expect(decoded.type, equals(type), reason: 'Failed for $type');
      }
    });

    test('all flag combinations round-trip', () {
      final flagValues = [
        ProtocolConstants.flagNone,
        ProtocolConstants.flagRequiresAck,
        ProtocolConstants.flagIsRetransmit,
        ProtocolConstants.flagPriority,
        ProtocolConstants.flagRequiresAck | ProtocolConstants.flagPriority,
      ];
      for (final flags in flagValues) {
        final msg = Message.withType(
          MessageType.message,
          Uint8List.fromList([1, 2, 3]),
        );
        msg.sequenceId = 42;
        msg.flags = flags;
        final encoded = MessageEncoder.encode(msg);
        final decoded = MessageEncoder.decode(encoded);
        // After decode, the Compressed flag is cleared (decompression happened),
        // so compare against the original flags.
        expect(decoded.flags, equals(flags));
      }
    });
  });

  // ── Round-trip: sequence ID edge cases ──────────────────────────────

  group('Round-trip: sequence ID edges', () {
    for (final seqId in [0, 1, 0x7FFFFFFF, 0xFFFFFFFF, 0x100000000]) {
      test('sequenceId = $seqId', () {
        final msg = Message.withType(MessageType.ack);
        msg.sequenceId = seqId;
        final encoded = MessageEncoder.encode(msg);
        final decoded = MessageEncoder.decode(encoded);
        expect(decoded.sequenceId, equals(seqId));
      });
    }
  });

  // ── CRC-32 utility tests ────────────────────────────────────────────

  group('CRC-32 header checksum', () {
    test('round-trip CRC matches', () {
      final msg = validMessage(payloadSize: 100);
      final encoded = MessageEncoder.encode(msg);
      final crc1 = MessageEncoder.computeHeaderCrc32(encoded);
      final crc2 = MessageEncoder.computeHeaderCrc32(encoded);
      expect(crc1, equals(crc2));
    });

    test('different headers produce different CRC', () {
      final msg1 = validMessage(payloadSize: 10);
      msg1.sequenceId = 1;
      final msg2 = validMessage(payloadSize: 10);
      msg2.sequenceId = 2;
      final enc1 = MessageEncoder.encode(msg1);
      final enc2 = MessageEncoder.encode(msg2);
      final crc1 = MessageEncoder.computeHeaderCrc32(enc1);
      final crc2 = MessageEncoder.computeHeaderCrc32(enc2);
      expect(crc1, isNot(equals(crc2)));
    });

    test('verifyHeaderCrc32 returns true for correct value', () {
      final msg = validMessage(payloadSize: 50);
      final encoded = MessageEncoder.encode(msg);
      final crc = MessageEncoder.computeHeaderCrc32(encoded);
      expect(MessageEncoder.verifyHeaderCrc32(encoded, crc), isTrue);
      expect(MessageEncoder.verifyHeaderCrc32(encoded, crc + 1), isFalse);
    });
  });

  // ── Buffer pool tests ───────────────────────────────────────────────

  group('BufferPool', () {
    test('acquire returns buffer of requested size or larger', () {
      final pool = BufferPool();
      final buf = pool.acquire(100);
      expect(buf.length, greaterThanOrEqualTo(100));
      pool.release(buf);
    });

    test('released buffer is zeroed', () {
      final pool = BufferPool();
      final buf = pool.acquire(16);
      buf.fillRange(0, 16, 0xFF);
      pool.release(buf);
      final reacquired = pool.acquire(16);
      // The reacquired buffer should be zeroed.
      expect(reacquired.every((b) => b == 0), isTrue);
    });

    test('pool size respects max', () {
      final pool = BufferPool(maxPoolSize: 2);
      pool.release(Uint8List(64));
      pool.release(Uint8List(64));
      pool.release(Uint8List(64)); // Should be discarded.
      expect(pool.pooledCount, equals(2));
    });
  });

  // ── Ring buffer tests ───────────────────────────────────────────────

  group('RingBuffer', () {
    test('write and take', () {
      final rb = RingBuffer(initialCapacity: 16);
      rb.write(Uint8List.fromList([1, 2, 3, 4]));
      expect(rb.length, equals(4));
      final taken = rb.take(4);
      expect(taken, equals([1, 2, 3, 4]));
      expect(rb.isEmpty, isTrue);
    });

    test('peekBytes returns zero-copy view', () {
      final rb = RingBuffer(initialCapacity: 16);
      rb.write(Uint8List.fromList([10, 20, 30, 40, 50]));
      final view = rb.peekBytes(1, 3);
      expect(view, equals([20, 30, 40]));
    });

    test('auto-grows on overflow', () {
      final rb = RingBuffer(initialCapacity: 4);
      rb.write(Uint8List.fromList(List.generate(100, (i) => i & 0xFF)));
      expect(rb.length, equals(100));
      expect(rb.capacity, greaterThanOrEqualTo(100));
      final data = rb.take(100);
      for (var i = 0; i < 100; i++) {
        expect(data[i], equals(i & 0xFF));
      }
    });

    test('compacts after consuming', () {
      final rb = RingBuffer(initialCapacity: 64);
      rb.write(Uint8List.fromList(List.filled(50, 0xAA)));
      rb.consume(40);
      expect(rb.length, equals(10));
      // After consuming more than half, it should compact.
      rb.write(Uint8List.fromList(List.filled(5, 0xBB)));
      expect(rb.length, equals(15));
    });
  });

  // ── Security utils tests ────────────────────────────────────────────

  group('SecureBufferUtils', () {
    test('zeroOut clears buffer', () {
      final buf = Uint8List.fromList([1, 2, 3, 4]);
      SecureBufferUtils.zeroOut(buf);
      expect(buf.every((b) => b == 0), isTrue);
    });

    test('secureRandomBytes returns correct length', () {
      final bytes = SecureBufferUtils.secureRandomBytes(32);
      expect(bytes.length, equals(32));
    });

    test('constantTimeEquals works correctly', () {
      final a = Uint8List.fromList([1, 2, 3]);
      final b = Uint8List.fromList([1, 2, 3]);
      final c = Uint8List.fromList([1, 2, 4]);
      final d = Uint8List.fromList([1, 2]);
      expect(SecureBufferUtils.constantTimeEquals(a, b), isTrue);
      expect(SecureBufferUtils.constantTimeEquals(a, c), isFalse);
      expect(SecureBufferUtils.constantTimeEquals(a, d), isFalse);
    });
  });

  // ── Encode validation tests ─────────────────────────────────────────

  group('Encode validation', () {
    test('rejects invalid magic', () {
      final msg = Message();
      msg.magic = 0xDEADBEEF;
      expect(
        () => MessageEncoder.encode(msg),
        throwsA(isA<ProtocolEncodeError>()),
      );
    });

    test('rejects invalid version', () {
      final msg = Message();
      msg.versionMajor = 99;
      expect(
        () => MessageEncoder.encode(msg),
        throwsA(isA<ProtocolEncodeError>()),
      );
    });

    test('rejects oversized payload', () {
      final msg = Message.withType(
        MessageType.message,
        Uint8List(ProtocolConstants.maxPayloadSize + 1),
      );
      expect(
        () => MessageEncoder.encode(msg),
        throwsA(isA<ProtocolEncodeError>()),
      );
    });
  });

  // ── Error type hierarchy tests ──────────────────────────────────────

  group('Error types', () {
    test('ProtocolDecodeError is a ProtocolError', () {
      final err = ProtocolDecodeError('test');
      expect(err, isA<ProtocolError>());
      expect(err, isA<Exception>());
    });

    test('ProtocolEncodeError is a ProtocolError', () {
      final err = ProtocolEncodeError('test');
      expect(err, isA<ProtocolError>());
    });

    test('ProtocolDecodeError includes hex dump', () {
      final data = Uint8List.fromList([0xDE, 0xAD, 0xBE, 0xEF]);
      final err = ProtocolDecodeError('bad frame', data);
      expect(err.toString(), contains('ProtocolDecodeError'));
      expect(err.hexDump, contains('de'));
      expect(err.hexDump, contains('ef'));
    });
  });
}
