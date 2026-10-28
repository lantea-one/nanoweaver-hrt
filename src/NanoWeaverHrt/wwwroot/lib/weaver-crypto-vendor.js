/*! noble-ed25519 - MIT License (c) 2019 Paul Miller (paulmillr.com) */
/**
 * 5KB JS implementation of ed25519 EdDSA signatures.
 * Targets RFC8032, FIPS 186-5, and ZIP215 behavior.
 * @module
 * @example
 * ```js
import * as ed from '@noble/ed25519';
(async () => {
  const secretKey = ed.utils.randomSecretKey();
  const message = Uint8Array.from([0xab, 0xbc, 0xcd, 0xde]);
  const pubKey = await ed.getPublicKeyAsync(secretKey); // Sync methods are also present
  const signature = await ed.signAsync(message, secretKey);
  const isValid = await ed.verifyAsync(signature, message, pubKey);
})();
```
 */
/**
 * Curve params. edwards25519 uses the RFC equation `-x² + y² = 1 + dx²y²`.
 * The stored `a` literal below is `p - 1`, i.e. the field-element encoding of RFC `a = -1`.
 * * P = `2n**255n - 19n` // field over which calculations are done
 * * N = `2n**252n + 27742317777372353535851937790883648493n` // prime-order subgroup order
 * * h = 8 // cofactor
 * * a = `Fp.create(BigInt(-1))` // equation param, stored here as `p - 1`
 * * d = -121665/121666 a.k.a. `Fp.neg(121665 * Fp.inv(121666))` // equation param
 * * Gx, Gy are coordinates of Generator / base point
 *
 * Mirror noble-curves: Point.CURVE() exposes shared params, but callers must not be able to mutate
 * that shared view and desynchronize it from the arithmetic constants captured below.
 */
const freeze = Object.freeze;
const P = 0x7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffedn;
const N$1 = 0x1000000000000000000000000000000014def9dea2f79cd65812631a5cf5d3edn;
const _d = 0x52036cee2b6ffe738cc740797779e89800700a4d4141d8ab75eb4dca135978a3n;
const Gx = 0x216936d3cd6e53fec0a4e231fdd6dc5c692cc7609525a7b2c9562d608f25d51an;
const Gy = 0x6666666666666666666666666666666666666666666666666666666666666658n;
const _a = P - 1n; // field-element encoding of RFC `a = -1`
const ed25519_CURVE = freeze({
    p: P,
    n: N$1,
    h: 8n,
    a: _a,
    d: _d,
    Gx,
    Gy,
});
// Shared 32-byte encoded width for Ed25519 points, scalars, signatures/2, and keys. Named LEN
// rather than RFC 8032's `L`, which names the group order (stored here as `N`).
const LEN = 32;
// Helpers and Precomputes sections are reused between libraries
// ## Helpers
// ----------
/** Checks if something is Uint8Array. Be careful: nodejs Buffer will return true. */
const isBytes$1 = (a) => {
    // Plain `instanceof Uint8Array` is too strict for some Buffer / proxy / cross-realm cases.
    // The fallback still requires a real ArrayBuffer view, so plain
    // JSON-deserialized `{ constructor: ... }` spoofing is rejected, and
    // `BYTES_PER_ELEMENT === 1` keeps the fallback on byte-oriented views.
    return (a instanceof Uint8Array ||
        (ArrayBuffer.isView(a) &&
            a.constructor.name === 'Uint8Array' &&
            a.BYTES_PER_ELEMENT === 1));
};
/** Asserts something is Bytes. */
const abytes$1 = (value, length, title = '') => {
    // Success path first: this runs at the start of every update() / digestInto(), and the
    // common `abytes(data)` form must not pay for length handling it does not use.
    if (isBytes$1(value) && (length === undefined || value.length === length))
        return value;
    // Error path: recompute freely to build the exact message.
    const bytes = isBytes$1(value);
    const ofLen = length !== undefined ? ` of length ${length}` : '';
    const got = bytes ? `length=${value.length}` : `type=${typeof value}`;
    const message = (title ? `"${title}" ` : '') + 'expected Uint8Array' + ofLen + ', got ' + got;
    if (!bytes)
        throw new TypeError(message);
    throw new RangeError(message);
};
// Validate, then take one owned copy, so later caller mutation cannot be observed — e.g. cannot
// make sign()'s nonce derivation and challenge derivation see different messages.
const snapshotBytes = (value, title = '', length) => Uint8Array.from(abytes$1(value, length, title));
// Callers keep values non-negative and within the requested width; padStart() won't truncate over-wide inputs.
const padh = (n, pad) => n.toString(16).padStart(pad, '0');
/** Convert byte array to hex string. */
const bytesToHex$1 = (bytes) => {
    let hex = '';
    for (const byte of abytes$1(bytes))
        hex += padh(byte, 2);
    return hex;
};
/** Convert hex string to byte array. */
const hexToBytes = (hex) => {
    const e = 'hex invalid'; // Strict ASCII hex only, with one generic error for parse failures.
    if (typeof hex !== 'string')
        throw new TypeError(e);
    if (hex.length % 2 || !/^[\da-f]*$/i.test(hex))
        throw new RangeError(e);
    const array = new Uint8Array(hex.length / 2);
    for (let ai = 0, hi = 0; ai < array.length; ai++, hi += 2) {
        const n1 = hex.charCodeAt(hi);
        const n2 = hex.charCodeAt(hi + 1);
        // Regex guarantees ASCII. For 0..9/A..F/a..f, this maps char codes to 0..15.
        array[ai] = ((n1 & 15) + (n1 >> 6) * 9) * 16 + (n2 & 15) + (n2 >> 6) * 9;
    }
    return array;
};
// WebCrypto is available in all modern environments
/** Copies several Uint8Arrays into one. */
const concatBytes$1 = (...arrays) => {
    let sum = 0;
    for (const a of arrays)
        sum += abytes$1(a).length;
    const res = new Uint8Array(sum);
    let pad = 0;
    for (const a of arrays) {
        res.set(a, pad);
        pad += a.length;
    }
    return res;
};
/** Inclusive-lower, exclusive-upper bigint range assertion. */
const arange = (n, min, max, msg = 'bad number: out of range') => {
    if (typeof n !== 'bigint')
        throw new TypeError(msg);
    if (min <= n && n < max)
        return n;
    throw new RangeError(msg);
};
/** Canonical modular reduction. Callers must provide a positive modulus. */
const mod = (a, b = P) => ((a %= b) >= 0n ? a : b + a);
// Low-255-bit mask used by the `2^255 - 19` fast reduction in `modP(...)`.
const P_MASK = (1n << 255n) - 1n;
// Fast reduction for the special prime `2^255 - 19`. This path assumes nonnegative inputs; the
// generic fallback would simply be `mod(num, P)`.
const modP = (num) => {
    if (num < 0n)
        throw new RangeError('negative coordinate');
    let r = (num >> 255n) * 19n + (num & P_MASK);
    r = (r >> 255n) * 19n + (r & P_MASK);
    return r % P;
};
// Reduce modulo the subgroup order stored in implementation constant `N` (RFC 8032's `L`).
const modN = (a) => mod(a, N$1);
/** Modular inversion using extended euclidean GCD. Variable-time (non-CT). */
const invert = (number, modulo) => {
    if (number === 0n)
        throw new Error('invert: expected non-zero number');
    // modulo = 1 is the zero ring: gcd(x, 1) = 1 makes the loop below "succeed" and return the
    // useless inverse 0. Reject it like pow() and invertCt() do.
    if (modulo <= 1n)
        throw new Error('invert: expected modulus > 1, got ' + modulo);
    // This is variable-time: the loop count depends on `number`.
    let a = mod(number, modulo);
    let b = modulo;
    // Only the Bézout coefficient of `number` (x/u chain) is tracked; the coefficient of `modulo`
    // never affects the output, so it is not computed.
    // prettier-ignore
    let x = 0n, u = 1n;
    while (a !== 0n) {
        const q = b / a;
        const r = b - a * q;
        const m = x - u * q;
        // prettier-ignore
        b = a, a = r, x = u, u = m;
    }
    const gcd = b;
    if (gcd !== 1n)
        throw new Error('invert: does not exist');
    return mod(x, modulo);
};
// Dynamic lookup keeps sync/async hash providers configurable at runtime. Both exported slots are
// caller-owned and may be unset; wrapper helpers use this lookup first and then enforce the digest
// contract instead of trusting provider output.
const _hash = (name) => {
    // @ts-ignore
    const fn = hashes[name];
    if (typeof fn !== 'function')
        throw new Error('hashes.' + name + ' not set');
    return fn;
};
// All exported provider slots are caller-configurable and may be unset or return arbitrary values,
// so wrapper helpers must enforce the exact 64-byte SHA-512 digest contract instead of trusting providers.
const callHash = (name, ...m) => abytes$1(_hash(name)(concatBytes$1(...m)), 64, 'digest');
// Runtime class guard: this is `instanceof Point`, so cross-realm / duplicate-bundle Point objects
// are rejected even if they are structurally identical.
const apoint = (p) => {
    if (p instanceof Point)
        return p;
    throw new TypeError('Point expected');
};
// ## End of Helpers
// -----------------
// Exclusive upper bound `2^256` used by 32-byte decode/serialization range checks.
const B256 = 2n ** 256n;
/**
 * Point in XYZT extended coordinates.
 * @param X - X coordinate.
 * @param Y - Y coordinate.
 * @param Z - Projective Z coordinate.
 * @param T - Cached cross-product term.
 * @example
 * Do point arithmetic with the built-in base point and encode the result as hex.
 *
 * ```ts
 * const hex = Point.BASE.double().toHex();
 * ```
 */
class Point {
    static BASE;
    static ZERO;
    X;
    Y;
    Z;
    T;
    // Constructor only bounds-checks and freezes XYZT coordinates; it does not prove the point is
    // on-curve or that T matches X*Y/Z.
    constructor(X, Y, Z, T) {
        const max = B256;
        this.X = arange(X, 0n, max);
        this.Y = arange(Y, 0n, max);
        this.Z = arange(Z, 1n, max);
        this.T = arange(T, 0n, max);
        freeze(this);
    }
    static CURVE() {
        return ed25519_CURVE;
    }
    static fromAffine(p) {
        return new Point(p.x, p.y, 1n, modP(p.x * p.y));
    }
    /** RFC8032 5.1.3: Uint8Array to Point. */
    static fromBytes(bytes, zip215 = false) {
        const normed = snapshotBytes(bytes, 'point', LEN); // owned copy: sign-bit mask below mutates it
        // adjust first LE byte = last BE byte
        const lastByte = normed[31];
        normed[31] = lastByte & -129;
        const y = bytesToNumberLE(normed);
        // After clearing the sign bit, parsed `y` is always < 2^255. ZIP-215 still accepts the full
        // post-mask range here, while strict RFC8032 decoding further requires `y < p`.
        if (!zip215)
            arange(y, 0n, P);
        const y2 = modP(y * y); // y²
        const u = mod(y2 - 1n); // u=y²-1
        const v = modP(_d * y2 + 1n); // v=dy²+1
        let { isValid, value: x } = uvRatio(u, v); // (uv³)(uv⁷)^(p-5)/8; square root
        if (!isValid)
            throw new Error('bad point: y not sqrt'); // not square root: bad point
        const isLastByteOdd = !!(lastByte & 0x80); // x_0, last bit
        // ZIP-215-compatible decoding keeps the x=0 / sign-bit=1 encoding accepted; strict RFC 8032
        // rejects it, but the vendored ZIP-215 compliance vectors include this form in A/R bytes.
        if (!zip215 && x === 0n && isLastByteOdd)
            throw new Error('bad point: x==0, isLastByteOdd'); // x=0, x_0=1
        if (isLastByteOdd !== !!(x & 1n))
            x = mod(-x); // adjust sign of x coordinate
        return new Point(x, y, 1n, modP(x * y)); // Z=1, T=xy
    }
    static fromHex(hex, zip215) {
        return Point.fromBytes(hexToBytes(hex), zip215);
    }
    get x() {
        return this.toAffine().x;
    }
    get y() {
        return this.toAffine().y;
    }
    /** Checks if the point is valid and on-curve. */
    assertValidity() {
        const a = _a;
        const d = _d;
        const p = this;
        // Intentional stricter-than-on-curve policy: reject ZERO by default because many protocols
        // require a non-zero point, and silently accepting identity points is a common caller mistake.
        if (p.is0())
            throw new Error('bad point: ZERO');
        // Equation in affine coordinates: ax² + y² = 1 + dx²y²
        // Equation in projective coordinates (X/Z, Y/Z, Z):  (aX² + Y²)Z² = Z⁴ + dX²Y²
        const { X, Y, Z, T } = p;
        const X2 = modP(X * X); // X²
        const Y2 = modP(Y * Y); // Y²
        const Z2 = modP(Z * Z); // Z²
        const Z4 = modP(Z2 * Z2); // Z⁴
        const aX2 = modP(X2 * a); // aX²
        const left = modP(Z2 * (aX2 + Y2)); // (aX² + Y²)Z²
        const right = mod(Z4 + modP(d * modP(X2 * Y2))); // Z⁴ + dX²Y²
        if (left !== right)
            throw new Error('bad point: equation left != right (1)');
        // In Extended coordinates we also have T, which is x*y=T/Z: check X*Y == Z*T
        const XY = modP(X * Y);
        const ZT = modP(Z * T);
        if (XY !== ZT)
            throw new Error('bad point: equation left != right (2)');
        return this;
    }
    /** Equality check: compare points P&Q. */
    equals(other) {
        const { X: X1, Y: Y1, Z: Z1 } = this;
        const { X: X2, Y: Y2, Z: Z2 } = apoint(other); // checks class equality
        return modP(X1 * Z2) === modP(X2 * Z1) && modP(Y1 * Z2) === modP(Y2 * Z1);
    }
    is0() {
        return this.equals(I);
    }
    /** Flip point over y coordinate. */
    negate() {
        return new Point(mod(-this.X), this.Y, this.Z, mod(-this.T));
    }
    /** Point doubling. Complete formula. Cost: `4M + 4S + 1*a + 6add + 1*2`. */
    double() {
        const { X: X1, Y: Y1, Z: Z1 } = this;
        const a = _a;
        // https://hyperelliptic.org/EFD/g1p/auto-twisted-extended.html#doubling-dbl-2008-hwcd
        const A = modP(X1 * X1);
        const B = modP(Y1 * Y1);
        const C = modP(2n * Z1 * Z1);
        const D = modP(a * A);
        const x1y1 = mod(X1 + Y1);
        const E = mod(modP(x1y1 * x1y1) - A - B);
        const G = mod(D + B);
        const F = mod(G - C);
        const H = mod(D - B);
        const X3 = modP(E * F);
        const Y3 = modP(G * H);
        const T3 = modP(E * H);
        const Z3 = modP(F * G);
        return new Point(X3, Y3, Z3, T3);
    }
    /** Point addition. Complete formula. Cost: `9M + 1*a + 1*d + 7add`. */
    add(other) {
        const { X: X1, Y: Y1, Z: Z1, T: T1 } = this;
        const { X: X2, Y: Y2, Z: Z2, T: T2 } = apoint(other); // doesn't check if other on-curve
        const a = _a;
        const d = _d;
        // https://hyperelliptic.org/EFD/g1p/auto-twisted-extended.html#addition-add-2008-hwcd
        const A = modP(X1 * X2);
        const B = modP(Y1 * Y2);
        const C = modP(modP(T1 * d) * T2);
        const D = modP(Z1 * Z2);
        const E = mod(modP(mod(X1 + Y1) * mod(X2 + Y2)) - A - B);
        const F = mod(D - C);
        const G = mod(D + C);
        const H = mod(B - modP(a * A));
        const X3 = modP(E * F);
        const Y3 = modP(G * H);
        const T3 = modP(E * H);
        const Z3 = modP(F * G);
        return new Point(X3, Y3, Z3, T3);
    }
    subtract(other) {
        return this.add(apoint(other).negate());
    }
    /**
     * Point-by-scalar multiplication. Safe mode requires `1 <= n < CURVE.n`.
     * Unsafe mode additionally permits `n = 0` and returns the identity point for that case.
     * Uses {@link wNAF} for base point.
     * Uses fake point to mitigate side-channel leakage.
     * @param n - scalar by which point is multiplied
     * @param safe - safe mode guards against timing attacks; unsafe mode is faster
     */
    multiply(n, safe = true) {
        // Mirror noble-curves: unsafe mode still validates scalar range first, but intentionally keeps
        // `n = 0` as the one extra accepted case used by verification-style callers.
        if (!safe && n === 0n)
            return I;
        arange(n, 1n, N$1);
        if (!safe && this.is0())
            return I;
        if (n === 1n)
            return this;
        if (this.equals(G))
            return wNAF(n).p;
        // init result point & fake point
        let p = I;
        let f = G;
        let d = this;
        // Safe mode always runs 256 iterations so ladder length can't leak the scalar's
        // leading zero bits; unsafe mode stops at the top set bit for speed.
        for (let i = 0; safe ? i < 256 : n > 0n; i++) {
            // if bit is present, add to point
            // if not present, add to fake, for timing safety
            if (n & 1n)
                p = p.add(d);
            else if (safe)
                f = f.add(d);
            d = d.double();
            n >>= 1n;
        }
        return p;
    }
    multiplyUnsafe(scalar) {
        return this.multiply(scalar, false);
    }
    /** Convert point to 2d xy affine point. (X, Y, Z) ∋ (x=X/Z, y=Y/Z) */
    toAffine() {
        const { X, Y, Z } = this;
        // Fast-path only for the identity point; all other inputs still go through inversion.
        if (this.equals(I))
            return { x: 0n, y: 1n };
        const iz = invert(Z, P);
        // (Z * Z^-1) must be 1, otherwise bad math
        if (modP(Z * iz) !== 1n)
            throw new Error('invalid inverse');
        // x = X*Z^-1; y = Y*Z^-1
        return { x: modP(X * iz), y: modP(Y * iz) };
    }
    toBytes() {
        const { x, y } = this.toAffine();
        const b = numTo32bLE(y);
        // store sign in first LE byte
        b[31] |= x & 1n ? 0x80 : 0;
        return b;
    }
    toHex() {
        return bytesToHex$1(this.toBytes());
    }
    clearCofactor() {
        return this.multiply(8n, false); // h = 8
    }
    isSmallOrder() {
        return this.clearCofactor().is0();
    }
    isTorsionFree() {
        // Multiply by big number N. We can't `mul(N)` because of checks. Instead, we `mul(N/2)*2+1`;
        // N is odd, so N/2 truncates and the final `add(this)` is always needed.
        return this.multiply(N$1 / 2n, false)
            .double()
            .add(this)
            .is0();
    }
}
/** Generator / base point */
const G = new Point(Gx, Gy, 1n, mod(Gx * Gy));
/** Identity / zero point */
const I = new Point(0n, 1n, 1n, 0n);
// Static aliases
Point.BASE = G;
Point.ZERO = I;
const numTo32bLE = (num) => hexToBytes(padh(arange(num, 0n, B256), 64)).reverse();
// Caller-enforced width: some sites require 32-byte RFC encodings, while others intentionally feed
// wider SHA-512 output chunks through the same little-endian parser.
const bytesToNumberLE = (b) => BigInt('0x' + bytesToHex$1(Uint8Array.from(abytes$1(b)).reverse()));
const pow2 = (x, power) => {
    // pow2(x, 4) == x^(2^4)
    // Negative `power` values are not rejected here and currently leave `x` unchanged.
    let r = x;
    while (power-- > 0) {
        r = modP(r * r);
    }
    return r;
};
// prettier-ignore
const pow_2_252_3 = (x) => {
    const x2 = modP(x * x); // x^2,       bits 1
    const b2 = modP(x2 * x); // x^3,       bits 11
    const b4 = modP(pow2(b2, 2) * b2); // x^(2^4-1), bits 1111
    const b5 = modP(pow2(b4, 1) * x); // x^(2^5-1), bits 11111
    const b10 = modP(pow2(b5, 5) * b5); // x^(2^10-1)
    const b20 = modP(pow2(b10, 10) * b10); // x^(2^20-1)
    const b40 = modP(pow2(b20, 20) * b20); // x^(2^40-1)
    const b80 = modP(pow2(b40, 40) * b40); // x^(2^80-1)
    const b160 = modP(pow2(b80, 80) * b80); // x^(2^160-1)
    const b240 = modP(pow2(b160, 80) * b80); // x^(2^240-1)
    const b250 = modP(pow2(b240, 10) * b10); // x^(2^250-1)
    return modP(pow2(b250, 2) * x); // x^((p-5)/8), used by RFC8032 point decode
};
const RM1 = 0x2b8324804fc1df0b2b4d00993dfbd7a72f431806ad2fe478c4ee1b274a0ea0b0n; // 2^((p-1)/4) = sqrt(-1)
// RFC8032 §5.1.3 square-root helper for point decompression. `value` is only meaningful when
// `isValid` is true; callers are also expected to pass canonical field elements with non-zero `v`.
// prettier-ignore
const uvRatio = (u, v) => {
    const v3 = modP(v * modP(v * v)); // v³
    const v7 = modP(modP(v3 * v3) * v); // v⁷
    const pow = pow_2_252_3(modP(u * v7)); // (uv⁷)^(p-5)/8
    let x = modP(u * modP(v3 * pow)); // (uv³)(uv⁷)^(p-5)/8
    const vx2 = modP(v * modP(x * x)); // vx²
    const root1 = x; // First root candidate
    const root2 = modP(x * RM1); // Second root candidate; RM1 is √-1
    const useRoot1 = vx2 === u; // If vx² = u (mod p), x is a square root
    const useRoot2 = vx2 === mod(-u); // If vx² = -u, set x <-- x * 2^((p-1)/4)
    const noRoot = vx2 === mod(-u * RM1); // There is no valid root, vx² = -u√-1
    if (useRoot1)
        x = root1;
    if (useRoot2 || noRoot)
        x = root2; // We return root2 anyway, for const-time
    if ((mod(x) & 1n) === 1n)
        x = mod(-x); // edIsNegative
    return { isValid: useRoot1 || useRoot2, value: x };
};
// Reduce a little-endian byte string modulo the subgroup order `N` (RFC 8032's `L`); the `hash`
// param name reflects the common caller shape, not an input restriction.
const modL_LE = (hash) => modN(bytesToNumberLE(hash));
// RFC8032 5.1.5. Split the 64-byte hashed seed into the clamped scalar half and nonce prefix.
const hashedToExtK = (hashed) => {
    // slice creates a copy, unlike subarray
    const copy = snapshotBytes(hashed);
    const head = copy.slice(0, 32);
    head[0] &= 248; // Clamp bits: 0b1111_1000
    head[31] &= 127; // 0b0111_1111
    head[31] |= 64; // 0b0100_0000
    const prefix = copy.slice(32); // secret key "prefix"
    // RFC words this as `[s]B`; reducing the clamped little-endian scalar modulo `N` is equivalent
    // for base-point multiplication because `G` already has subgroup order `N`.
    const scalar = modL_LE(head);
    const point = G.multiply(scalar); // public key point
    const pointBytes = point.toBytes(); // point serialized to Uint8Array
    return { head, prefix, scalar, point, pointBytes };
};
const getExtendedPublicKey = (secretKey) => hashedToExtK(callHash('sha512', abytes$1(secretKey, LEN, 'secretKey')));
const hashFinishSync = (res) => res[1](callHash('sha512', res[0]));
// Code, shared between sync & async sign
const _sign = (e, rBytes, msg) => {
    const { pointBytes: A, scalar: s } = e;
    const r = modL_LE(rBytes); // r was created outside, reduce it modulo L
    // RFC 8032 5.1.6 allows r mod L = 0, and SUPERCOP ref10 accepts the resulting identity-point
    // signature.
    // We intentionally keep the safe multiply() rejection here so a miswired all-zero SHA-512 provider
    // fails loudly instead of silently producing a degenerate signature.
    const R = G.multiply(r).toBytes(); // R = [r]B
    const hashable = concatBytes$1(R, A, msg); // dom2(F, C) || R || A || PH(M)
    const finish = (hashed) => {
        // k = SHA512(dom2(F, C) || R || A || PH(M))
        const S = modN(r + modL_LE(hashed) * s); // S = (r + k * s) mod L; 0 <= s < l
        return abytes$1(concatBytes$1(R, numTo32bLE(S)), 64); // 64-byte sig: 32-byte encoded R point || 32-byte LE(S)
    };
    return [hashable, finish];
};
/**
 * Signs message using secret key. To use, set `hashes.sha512` first.
 * Follows RFC8032 5.1.6.
 * @param message - Message bytes to sign.
 * @param secretKey - 32-byte RFC 8032 secret-key seed, not a 64-byte expanded secret key.
 * @returns 64-byte Ed25519 signature.
 * @throws If synchronous SHA-512 has not been configured in `hashes`. {@link Error}
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Use the sync API when you've wired a SHA-512 implementation yourself.
 *
 * ```ts
 * import * as ed from '@noble/ed25519';
 * import { sha512 } from '@noble/hashes/sha2.js';
 *
 * ed.hashes.sha512 = sha512;
 * const secretKey = ed.utils.randomSecretKey();
 * const signature = ed.sign(new Uint8Array([1, 2, 3]), secretKey);
 * ```
 */
const sign = (message, secretKey) => {
    const m = snapshotBytes(message, 'message');
    const e = getExtendedPublicKey(secretKey);
    // r = SHA512(dom2(F, C) || prefix || PH(M)); then generate R, k, S and the signature.
    return hashFinishSync(_sign(e, callHash('sha512', e.prefix, m), m));
};
/**
 * Hash implementations used by the synchronous API plus the default async WebCrypto provider.
 * Both slots are configurable API surface; wrapper helpers revalidate that providers still return
 * 64-byte SHA-512 digests.
 * @example
 * Provide a SHA-512 implementation before calling synchronous helpers.
 *
 * ```ts
 * import * as ed from '@noble/ed25519';
 * import { sha512 } from '@noble/hashes/sha2.js';
 *
 * ed.hashes.sha512 = sha512;
 * const { publicKey } = ed.keygen();
 * ```
 */
const hashes = {
    sha512Async: async (message) => {
        const s = globalThis?.crypto?.subtle;
        if (!s)
            throw new Error('crypto.subtle must be defined, consider polyfill');
        return new Uint8Array(await s.digest('SHA-512', concatBytes$1(message)));
    },
    sha512: undefined,
};
// ## Precomputes
// --------------
// Fixed W=8 window: 33 windows (ceil(256/8) + 1, NOT 32 - see wNAF loop), 128 points per window.
// Layout is grouped by window: each block stores the positive multiples `1*base .. 128*base` for
// that window, and the extra `+1` window absorbs carries from signed-digit recoding.
const precompute = () => {
    const points = [];
    let p = G;
    let b;
    for (let w = 0; w < 33; w++) {
        b = p;
        points.push(b);
        for (let i = 1; i < 128; i++) {
            b = b.add(p);
            points.push(b);
        } // i=1, bc we skip 0
        p = b.double();
    }
    return points;
};
let Gpows = undefined; // shared process-wide cache of base-point precomputes
// Branch-based negate helper used for JS/JIT mitigation symmetry, not a strict constant-time claim.
const ctneg = (cnd, p) => {
    const n = p.negate();
    return cnd ? n : p;
};
/**
 * Precomputes give 12x faster getPublicKey(), 10x sign(), 2x verify() by
 * caching multiples of G (base point). Cache is stored in 32MB of RAM.
 * Any time `G.multiply` is done, precomputes are used.
 * Not used for getSharedSecret, which instead multiplies random pubkey `P.multiply`.
 *
 * w-ary non-adjacent form (wNAF) precomputation method is 10% slower than windowed method,
 * but takes 2x less RAM. RAM reduction is possible by utilizing `.subtract`.
 * Returns the real accumulator `p` plus a fake accumulator `f`; callers only care about `p`, while
 * `f` exists to keep similar work in zero-digit branches as a JS/JIT side-channel mitigation.
 *
 * !! Precomputes can be disabled by commenting-out call of the wNAF() inside Point#multiply().
 */
const wNAF = (n) => {
    const comp = Gpows || (Gpows = precompute());
    let p = I;
    let f = G; // f must be G, or could become I in the end
    for (let w = 0; w < 33; w++) {
        let wbits = Number(n & 255n); // extract W=8 bits.
        n >>= 8n; // shift number by W=8 bits.
        // We use negative indexes to reduce size of precomputed table by 2x.
        // Instead of needing precomputes 0..256, we only calculate them for 0..128.
        // If an index > 128 is found, we do (256-index) - where 256 is next window.
        // Naive: index +127 => 127, +224 => 224
        // Optimized: index +127 => 127, +224 => 256-32
        if (wbits > 128) {
            wbits -= 256;
            n += 1n;
        }
        const off = w * 128; // offset of this window's block; also the fake-add index
        const offP = off + Math.abs(wbits) - 1; // real-add index; both offsets always evaluated
        const isOddW = w % 2 !== 0; // conditions, evaluate both; alternates fake-add sign per window
        const isNeg = wbits < 0;
        if (wbits === 0) {
            // wbits == 0 means the real index would be I: can't add it. Add window offset `off` instead.
            f = f.add(ctneg(isOddW, comp[off])); // bits are 0: add garbage to fake point
        }
        else {
            p = p.add(ctneg(isNeg, comp[offP])); // bits are 1: add to result point
        }
    }
    if (n !== 0n)
        throw new Error('invalid wnaf');
    return { p, f }; // return both real and fake points for JIT/leakage-shape symmetry
};

const U32_MASK64 = /* @__PURE__ */ (() => BigInt(2 ** 32 - 1))();
const _32n = /* @__PURE__ */ BigInt(32);
// Split bigint into two 32-bit halves. With `le=true`, returned fields become `{ h: low, l: high
// }` to match little-endian word order rather than the property names.
function fromBig(n, le = false) {
    if (le)
        return { h: Number(n & U32_MASK64), l: Number((n >> _32n) & U32_MASK64) };
    return { h: Number((n >> _32n) & U32_MASK64) | 0, l: Number(n & U32_MASK64) | 0 };
}
// Split bigint list into `[highWords, lowWords]` when `le=false`; with `le=true`, the first array
// holds the low halves because `fromBig(...)` swaps the semantic meaning of `h` and `l`.
function split(lst, le = false) {
    const len = lst.length;
    let Ah = new Uint32Array(len);
    let Al = new Uint32Array(len);
    for (let i = 0; i < len; i++) {
        const { h, l } = fromBig(lst[i], le);
        [Ah[i], Al[i]] = [h, l];
    }
    return [Ah, Al];
}
// Split a JS number into u32 halves without a BigInt allocation. Exact only for integers
// `0 <= n < 2**53`; callers use it on byte / bit counters, which JS length math caps far below
// that (an ArrayBuffer cannot exceed 2**53 - 1 bytes).
const fromNumH = (n) => (n / 2 ** 32) | 0;
const fromNumL = (n) => n >>> 0;
// Drop-in replacement for `view.setBigUint64(byteOffset, BigInt(n), isLE)` without the per-call
// BigInt allocation. Same `n < 2**53` precondition as `fromNumH`/`fromNumL`.
function setU64FromNum(view, byteOffset, n, isLE) {
    const h = fromNumH(n);
    const l = fromNumL(n);
    view.setUint32(byteOffset, isLE ? l : h, isLE);
    view.setUint32(byteOffset + 4, isLE ? h : l, isLE);
}
// High 32-bit half of a 64-bit logical right shift for `s` in `0..31`.
const shrSH = (h, _l, s) => h >>> s;
// Low 32-bit half of a 64-bit logical right shift, valid for `s` in `1..31`.
const shrSL = (h, l, s) => (h << (32 - s)) | (l >>> s);
// High 32-bit half of a 64-bit right rotate, valid for `s` in `1..31`.
const rotrSH = (h, l, s) => (h >>> s) | (l << (32 - s));
// Low 32-bit half of a 64-bit right rotate, valid for `s` in `1..31`.
const rotrSL = (h, l, s) => (h << (32 - s)) | (l >>> s);
// High 32-bit half of a 64-bit right rotate, valid for `s` in `33..63`; `32` uses `rotr32*`.
const rotrBH = (h, l, s) => (h << (64 - s)) | (l >>> (s - 32));
// Low 32-bit half of a 64-bit right rotate, valid for `s` in `33..63`; `32` uses `rotr32*`.
const rotrBL = (h, l, s) => (h >>> (s - 32)) | (l << (64 - s));
// 64-bit left rotates (rotl*) are not defined here: sha3.ts, their only consumer, keeps
// local copies so V8 inlines them into keccakP.
// Add two split 64-bit words and return the split `{ h, l }` sum.
// JS uses 32-bit signed integers for bitwise operations, so we cannot simply shift the carry out
// of the low sum and instead use division.
function add(Ah, Al, Bh, Bl) {
    const l = (Al >>> 0) + (Bl >>> 0);
    return { h: (Ah + Bh + ((l / 2 ** 32) | 0)) | 0, l: l | 0 };
}
// Addition with more than 2 elements
// Unmasked low-word accumulator for 3-way addition; pass the raw result into `add3H(...)`.
const add3L = (Al, Bl, Cl) => (Al >>> 0) + (Bl >>> 0) + (Cl >>> 0);
// High-word finalize step for 3-way addition; `low` must be the untruncated output of `add3L(...)`.
const add3H = (low, Ah, Bh, Ch) => (Ah + Bh + Ch + ((low / 2 ** 32) | 0)) | 0;
// Unmasked low-word accumulator for 4-way addition; pass the raw result into `add4H(...)`.
const add4L = (Al, Bl, Cl, Dl) => (Al >>> 0) + (Bl >>> 0) + (Cl >>> 0) + (Dl >>> 0);
// High-word finalize step for 4-way addition; `low` must be the untruncated output of `add4L(...)`.
const add4H = (low, Ah, Bh, Ch, Dh) => (Ah + Bh + Ch + Dh + ((low / 2 ** 32) | 0)) | 0;
// Unmasked low-word accumulator for 5-way addition; pass the raw result into `add5H(...)`.
const add5L = (Al, Bl, Cl, Dl, El) => (Al >>> 0) + (Bl >>> 0) + (Cl >>> 0) + (Dl >>> 0) + (El >>> 0);
// High-word finalize step for 5-way addition; `low` must be the untruncated output of `add5L(...)`.
const add5H = (low, Ah, Bh, Ch, Dh, Eh) => (Ah + Bh + Ch + Dh + Eh + ((low / 2 ** 32) | 0)) | 0;

/**
 * Checks if something is Uint8Array. Be careful: nodejs Buffer will return true.
 * @param a - value to test
 * @returns `true` when the value is a Uint8Array-compatible view.
 * @example
 * Check whether a value is a Uint8Array-compatible view.
 * ```ts
 * isBytes(new Uint8Array([1, 2, 3]));
 * ```
 */
function isBytes(a) {
    // Plain `instanceof Uint8Array` is too strict for some Buffer / proxy / cross-realm cases.
    // The fallback still requires a real ArrayBuffer view, so plain
    // JSON-deserialized `{ constructor: ... }` spoofing is rejected, and
    // `BYTES_PER_ELEMENT === 1` keeps the fallback on byte-oriented views.
    return (a instanceof Uint8Array ||
        (ArrayBuffer.isView(a) &&
            a.constructor.name === 'Uint8Array' &&
            'BYTES_PER_ELEMENT' in a &&
            a.BYTES_PER_ELEMENT === 1));
}
// Shared error-message prefix builder. Only called on throw paths, so assert
// success paths never pay for the string concatenation.
const atitle$1 = (title) => (title ? `"${title}" ` : '');
/**
 * Asserts something is a non-negative integer.
 * @param n - number to validate
 * @param title - label included in thrown errors
 * @returns The validated number.
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Validate a non-negative integer option.
 * ```ts
 * anumber(32, 'length');
 * ```
 */
function anumber(n, title = '') {
    if (typeof n !== 'number')
        throw new TypeError(atitle$1(title) + 'expected number, got ' + typeof n);
    if (!Number.isSafeInteger(n) || n < 0)
        throw new RangeError(atitle$1(title) + 'expected integer >= 0, got ' + n);
    return n;
}
/**
 * Asserts something is a boolean.
 * @param value - value to validate
 * @param title - label included in thrown errors
 * @returns The validated boolean.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Validate a boolean option.
 * ```ts
 * abool(true, 'enableXOF');
 * ```
 */
function abool$1(value, title = '') {
    if (typeof value !== 'boolean')
        throw new TypeError(atitle$1(title) + 'expected boolean, got type=' + typeof value);
    return value;
}
/**
 * Asserts something is Uint8Array.
 * @param value - value to validate
 * @param length - optional exact length constraint
 * @param title - label included in thrown errors
 * @returns The validated byte array.
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Validate that a value is a byte array.
 * ```ts
 * abytes(new Uint8Array([1, 2, 3]));
 * ```
 */
function abytes(value, length, title = '') {
    // Success path first: this runs at the start of every update() / digestInto(), and the
    // common `abytes(data)` form must not pay for length handling it does not use.
    if (isBytes(value) && (length === undefined || value.length === length))
        return value;
    // Error path: recompute freely to build the exact message.
    if (length !== undefined)
        anumber(length, 'length');
    const bytes = isBytes(value);
    const ofLen = length !== undefined ? ` of length ${length}` : '';
    const got = bytes ? `length=${value.length}` : `type=${typeof value}`;
    const message = atitle$1(title) + 'expected Uint8Array' + ofLen + ', got ' + got;
    if (!bytes)
        throw new TypeError(message);
    throw new RangeError(message);
}
/**
 * Asserts something is a wrapped hash constructor.
 * @param h - hash constructor to validate
 * @throws On wrong argument types or invalid hash wrapper shape. {@link TypeError}
 * @throws On invalid hash metadata ranges or values. {@link RangeError}
 * @throws If the hash metadata allows empty outputs or block sizes. {@link Error}
 * @example
 * Validate a callable hash wrapper.
 * ```ts
 * import { ahash } from '@noble/hashes/utils.js';
 * import { sha256 } from '@noble/hashes/sha2.js';
 * ahash(sha256);
 * ```
 */
function ahash(h) {
    if (typeof h !== 'function' || typeof h.create !== 'function')
        throw new TypeError('expected hash wrapped by utils.createHasher');
    anumber(h.outputLen);
    anumber(h.blockLen);
    // HMAC and KDF callers treat these as real byte lengths; allowing zero lets fake wrappers pass
    // validation and can produce empty outputs instead of failing fast.
    if (h.outputLen < 1 || h.blockLen < 1)
        throw new Error('hash blockLen / outputLen must be >= 1');
}
const aobject$2 = (value, label) => {
    if (value === null || typeof value !== 'object' || Array.isArray(value))
        throw new TypeError((label === 'object' ? '' : `"${label}" `) + 'expected object, got type=' + typeof value);
};
const aopts = (value, label) => {
    aobject$2(value, label);
    const proto = Object.getPrototypeOf(value);
    if (proto !== Object.prototype && proto !== null)
        throw new TypeError(`"${label}" expected plain object`);
    // Object.assign() treats an own "__proto__" source key as a write to the target's legacy
    // prototype setter. Reject it before merging so inherited option values cannot be injected.
    if (Object.hasOwn(value, '__proto__'))
        throw new TypeError(`"${label}.__proto__" is not allowed`);
};
/**
 * Asserts a hash instance has not been destroyed or finished.
 * @param instance - hash instance to validate
 * @param checkFinished - whether to reject finalized instances
 * @throws If the hash instance has already been destroyed or finalized. {@link Error}
 * @example
 * Validate that a hash instance is still usable.
 * ```ts
 * import { aexists } from '@noble/hashes/utils.js';
 * import { sha256 } from '@noble/hashes/sha2.js';
 * const hash = sha256.create();
 * aexists(hash);
 * ```
 */
function aexists(instance, checkFinished = true) {
    // Runs on every update()/digestInto(); the flags are library-owned booleans, so only their
    // truthiness is checked - re-validating their type per call was pure hot-path overhead.
    if (instance.destroyed)
        throw new Error('hash was destroyed');
    if (checkFinished && instance.finished)
        throw new Error('digest() was already called');
}
/**
 * Asserts output is a sufficiently-sized byte array.
 * @param out - destination buffer
 * @param instance - hash instance providing output length
 * Oversized buffers are allowed; downstream code only promises to fill the first `outputLen` bytes.
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Validate a caller-provided digest buffer.
 * ```ts
 * import { aoutput } from '@noble/hashes/utils.js';
 * import { sha256 } from '@noble/hashes/sha2.js';
 * const hash = sha256.create();
 * aoutput(new Uint8Array(hash.outputLen), hash);
 * ```
 */
function aoutput(out, instance) {
    abytes(out, undefined, 'output');
    // `outputLen` is a library-owned readonly number; the negated comparison keeps failing fast
    // when it is missing/NaN (comparisons with undefined/NaN are false) without an anumber() call.
    const min = instance.outputLen;
    if (!(out.length >= min)) {
        throw new RangeError('"output" expected length >= ' + min);
    }
}
/**
 * Casts a typed array view to Uint32Array.
 * `arr.byteOffset` must already be 4-byte aligned or the platform
 * Uint32Array constructor will throw.
 * @param arr - source typed array
 * @returns Uint32Array view over the same buffer.
 * @example
 * Reinterpret a byte array as 32-bit words.
 * ```ts
 * u32(new Uint8Array(8));
 * ```
 */
function u32(arr) {
    return new Uint32Array(arr.buffer, arr.byteOffset, Math.floor(arr.byteLength / 4));
}
/**
 * Zeroizes typed arrays in place. Warning: JS provides no guarantees.
 * @param arrays - arrays to overwrite with zeros
 * @example
 * Zeroize sensitive buffers in place.
 * ```ts
 * clean(new Uint8Array([1, 2, 3]));
 * ```
 */
function clean(...arrays) {
    for (let i = 0; i < arrays.length; i++) {
        arrays[i].fill(0);
    }
}
/**
 * Creates a DataView for byte-level manipulation.
 * @param arr - source typed array
 * @returns DataView over the same buffer region.
 * @example
 * Create a DataView over an existing buffer.
 * ```ts
 * createView(new Uint8Array(4));
 * ```
 */
function createView(arr) {
    return new DataView(arr.buffer, arr.byteOffset, arr.byteLength);
}
/** Whether the current platform is little-endian. */
const isLE = /* @__PURE__ */ (() => new Uint8Array(new Uint32Array([0x11223344]).buffer)[0] === 0x44)();
/**
 * Byte-swap operation for uint32 values.
 * @param word - source word
 * @returns Word with reversed byte order.
 * @example
 * Reverse the byte order of a 32-bit word.
 * ```ts
 * byteSwap(0x11223344);
 * ```
 */
function byteSwap(word) {
    return (((word << 24) & 0xff000000) |
        ((word << 8) & 0xff0000) |
        ((word >>> 8) & 0xff00) |
        ((word >>> 24) & 0xff));
}
/**
 * Byte-swaps every word of a Uint32Array in place.
 * @param arr - array to mutate
 * @returns The same array after mutation; callers pass live state arrays here.
 * @example
 * Reverse the byte order of every word in place.
 * ```ts
 * byteSwap32(new Uint32Array([0x11223344]));
 * ```
 */
function byteSwap32(arr) {
    for (let i = 0; i < arr.length; i++) {
        arr[i] = byteSwap(arr[i]);
    }
    return arr;
}
/**
 * Conditionally byte-swaps a Uint32Array on big-endian platforms.
 * @param u - array to normalize for host endianness
 * @returns Original or byte-swapped array depending on platform endianness.
 *   On big-endian runtimes this mutates `u` in place via `byteSwap32(...)`.
 * @example
 * Normalize a word array for host endianness.
 * ```ts
 * swap32IfBE(new Uint32Array([0x11223344]));
 * ```
 */
const swap32IfBE = isLE
    ? (u) => u
    : byteSwap32;
// Built-in hex conversion https://caniuse.com/mdn-javascript_builtins_uint8array_fromhex
const hasHexBuiltin = /* @__PURE__ */ (() => 
// @ts-ignore
typeof Uint8Array.from([]).toHex === 'function' && typeof Uint8Array.fromHex === 'function')();
// Array where index 0xf0 (240) is mapped to string 'f0'
const hexes = /* @__PURE__ */ Array.from({ length: 256 }, (_, i) => i.toString(16).padStart(2, '0'));
/**
 * Convert byte array to hex string.
 * Uses the built-in function when available and assumes it matches the tested
 * fallback semantics.
 * @param bytes - bytes to encode
 * @returns Lowercase hexadecimal string.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Convert bytes to lowercase hexadecimal.
 * ```ts
 * bytesToHex(Uint8Array.from([0xca, 0xfe, 0x01, 0x23])); // 'cafe0123'
 * ```
 */
function bytesToHex(bytes) {
    abytes(bytes);
    // @ts-ignore
    if (hasHexBuiltin)
        return bytes.toHex();
    // pre-caching improves the speed 6x
    let hex = '';
    for (let i = 0; i < bytes.length; i++) {
        hex += hexes[bytes[i]];
    }
    return hex;
}
/**
 * Copies several Uint8Arrays into one.
 * @param arrays - arrays to concatenate
 * @returns Concatenated byte array.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Concatenate multiple byte arrays.
 * ```ts
 * concatBytes(new Uint8Array([1]), new Uint8Array([2]));
 * ```
 */
function concatBytes(...arrays) {
    let sum = 0;
    for (let i = 0; i < arrays.length; i++) {
        const a = arrays[i];
        abytes(a);
        sum += a.length;
    }
    const res = new Uint8Array(sum);
    for (let i = 0, pad = 0; i < arrays.length; i++) {
        const a = arrays[i];
        res.set(a, pad);
        pad += a.length;
    }
    return res;
}
/**
 * Merges default options and passed options.
 * @param defaults - base option object
 * @param opts - user overrides
 * @param title - label included in thrown override errors
 * @returns Fresh merged option object with a null prototype.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Merge user overrides onto default options.
 * ```ts
 * checkOpts({ dkLen: 32 }, { asyncTick: 10 });
 * ```
 */
function checkOpts(defaults, opts, title = 'opts') {
    aopts(defaults, 'defaults');
    if (opts !== undefined)
        aopts(opts, title);
    // Callers read optional fields directly, so omitted values must not fall through to ambient
    // Object.prototype pollution (for example a forged `dkLen` changing SHAKE's default output).
    const merged = Object.assign(Object.create(null), defaults, opts);
    return merged;
}
/**
 * Creates a callable hash function from a stateful class constructor.
 * @param hashCons - hash constructor or factory
 * @param info - optional metadata such as DER OID
 * @returns Frozen callable hash wrapper with `.create()`.
 *   Wrapper construction eagerly calls `hashCons(undefined)` once to read
 *   `outputLen` / `blockLen`, so constructor side effects happen at module
 *   init time.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Wrap a stateful hash constructor into a callable helper.
 * ```ts
 * import { createHasher } from '@noble/hashes/utils.js';
 * import { sha256 } from '@noble/hashes/sha2.js';
 * const wrapped = createHasher(sha256.create, { oid: sha256.oid });
 * wrapped(new Uint8Array([1]));
 * ```
 */
function createHasher(hashCons, info = {}) {
    if (typeof hashCons !== 'function')
        throw new TypeError('"hashCons" expected function, got type=' + typeof hashCons);
    info = checkOpts({}, info, 'info');
    const hashC = (msg, opts) => hashCons(opts)
        .update(msg)
        .digest();
    const tmp = hashCons(undefined);
    hashC.outputLen = tmp.outputLen;
    hashC.blockLen = tmp.blockLen;
    hashC.canXOF = tmp.canXOF;
    hashC.create = (opts) => hashCons(opts);
    Object.assign(hashC, info);
    return Object.freeze(hashC);
}
/**
 * Cryptographically secure PRNG backed by `crypto.getRandomValues`.
 * @param bytesLength - number of random bytes to generate
 * @returns Random bytes.
 * The platform `getRandomValues()` implementation still defines any
 * single-call length cap, and this helper rejects oversize requests
 * with a stable library `RangeError` instead of host-specific errors.
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @throws If the current runtime does not provide `crypto.getRandomValues`. {@link Error}
 * @example
 * Generate a fresh random key or nonce.
 * ```ts
 * const key = randomBytes(16);
 * ```
 */
function randomBytes$1(bytesLength = 32) {
    // Match the repo's other length-taking helpers instead of relying on Uint8Array coercion.
    anumber(bytesLength, 'bytesLength');
    const cr = typeof globalThis === 'object' ? globalThis.crypto : null;
    if (typeof cr?.getRandomValues !== 'function')
        throw new Error('crypto.getRandomValues must be defined');
    // Web Cryptography API Level 2 §10.1.1:
    // if `byteLength > 65536`, throw `QuotaExceededError`.
    // Keep the guard explicit so callers can see the quota in code
    // instead of discovering it by reading the spec or host errors.
    // This wrapper surfaces the same quota as a stable library RangeError.
    if (bytesLength > 65536)
        throw new RangeError(`"bytesLength" expected <= 65536, got ${bytesLength}`);
    return cr.getRandomValues(new Uint8Array(bytesLength));
}
/**
 * Creates OID metadata for NIST hashes with prefix `06 09 60 86 48 01 65 03 04 02`.
 * @param suffix - final OID byte for the selected hash.
 *   The helper accepts any byte even though only the documented NIST hash
 *   suffixes are meaningful downstream.
 * @returns Object containing the DER-encoded OID.
 * @example
 * Build OID metadata for a NIST hash.
 * ```ts
 * oidNist(0x01);
 * ```
 */
const oidNist = (suffix) => ({
    // Current NIST hashAlgs suffixes used here fit in one DER subidentifier octet.
    // Larger suffix values would need base-128 OID encoding and a different length byte.
    oid: Uint8Array.from([0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, suffix]),
});

/**
 * Internal Merkle-Damgard hash utils.
 * @module
 */
/**
 * Merkle-Damgard hash construction base class.
 * Could be used to create MD5, RIPEMD, SHA1, SHA2.
 * Accepts only byte-aligned `Uint8Array` input, even when the underlying spec describes bit
 * strings with partial-byte tails.
 * @param blockLen - internal block size in bytes
 * @param outputLen - digest size in bytes
 * @param padOffset - trailing length field size in bytes
 * @param isLE - whether length and state words are encoded in little-endian
 * @example
 * Use a concrete subclass to get the shared Merkle-Damgard update/digest flow.
 * ```ts
 * import { _SHA1 } from '@noble/hashes/legacy.js';
 * const hash = new _SHA1();
 * hash.update(new Uint8Array([97, 98, 99]));
 * hash.digest();
 * ```
 */
class HashMD {
    blockLen;
    outputLen;
    canXOF = false;
    padOffset;
    isLE;
    // For partial updates less than block size
    buffer;
    view;
    finished = false;
    length = 0;
    pos = 0;
    destroyed = false;
    constructor(blockLen, outputLen, padOffset, isLE) {
        this.blockLen = blockLen;
        this.outputLen = outputLen;
        this.padOffset = padOffset;
        this.isLE = isLE;
        this.buffer = new Uint8Array(blockLen);
        this.view = createView(this.buffer);
    }
    update(data) {
        aexists(this);
        abytes(data);
        const { view, buffer, blockLen } = this;
        const len = data.length;
        let processed = false;
        for (let pos = 0; pos < len;) {
            const take = Math.min(blockLen - this.pos, len - pos);
            // Fast path only when there is no buffered partial block: `take === blockLen` implies
            // `this.pos === 0`, so we can process full blocks directly from the input view.
            if (take === blockLen) {
                const dataView = createView(data);
                for (; blockLen <= len - pos; pos += blockLen)
                    this.process(dataView, pos);
                processed = true;
                continue;
            }
            // When the whole input is buffered in one go (common for short messages), passing `data`
            // directly avoids allocating a subarray view.
            buffer.set(pos === 0 && take === len ? data : data.subarray(pos, pos + take), this.pos);
            this.pos += take;
            pos += take;
            if (this.pos === blockLen) {
                this.process(view, 0);
                this.pos = 0;
                processed = true;
            }
        }
        this.length += data.length;
        // Shared schedule buffers only pick up input-derived words inside process(); if everything
        // was buffered without processing, there is nothing to zero.
        if (processed)
            this.roundClean();
        return this;
    }
    digestInto(out) {
        aexists(this);
        aoutput(out, this);
        this.finished = true;
        // Padding
        // We can avoid allocation of buffer for padding completely if it
        // was previously not allocated here. But it won't change performance.
        const { buffer, view, blockLen, isLE } = this;
        let { pos } = this;
        // append the bit '1' to the message, then zero-pad the rest of the block
        buffer[pos++] = 0b10000000;
        buffer.fill(0, pos);
        // we have less than padOffset left in buffer, so we cannot put length in
        // current block, need process it and pad again
        if (this.padOffset > blockLen - pos) {
            this.process(view, 0);
            buffer.fill(0);
        }
        // `padOffset` reserves the whole length field. For SHA-384/512 the high 64 bits stay zero from
        // the padding fill above, and JS will overflow before user input can make that half non-zero.
        // So we only need to write the low 64 bits here (`length * 8` only scales the exponent of an
        // integer below 2**53, so the split inside the helper stays exact).
        setU64FromNum(view, blockLen - 8, this.length * 8, isLE);
        this.process(view, 0);
        // The final block above is processed outside update(), so the shared message-schedule
        // buffers (e.g. SHA256_W) would otherwise retain input-derived words after digest().
        this.roundClean();
        // digest() passes our own `buffer` as `out`; reuse its cached view instead of allocating one.
        const oview = out === buffer ? view : createView(out);
        const len = this.outputLen;
        // NOTE: we do division by 4 later, which must be fused in single op with modulo by JIT
        const outLen = len / 4;
        const state = this.get();
        // Subclass-misconfiguration invariant: outputLen must be 32-bit aligned and fit the state.
        if (len % 4 || outLen > state.length)
            throw new Error('invalid outputLen');
        for (let i = 0; i < outLen; i++)
            oview.setUint32(4 * i, state[i], isLE);
    }
    digest() {
        const { buffer, outputLen } = this;
        this.digestInto(buffer);
        // Copy before destroy(): subclasses wipe `buffer` during cleanup, but `digest()` must return
        // fresh bytes to the caller.
        const res = buffer.slice(0, outputLen);
        this.destroy();
        return res;
    }
    _cloneIntoMeta(to) {
        const { buffer, length, finished, destroyed, pos } = this;
        to.destroyed = destroyed;
        to.finished = finished;
        to.length = length;
        to.pos = pos;
        // Only partial-block bytes need copying: when `length % blockLen === 0`, `pos === 0` and
        // later `update()` / `digestInto()` overwrite `to.buffer` from the start before reading it.
        if (pos)
            to.buffer.set(buffer); // Avoid a hot modulo guard.
        return to;
    }
    clone() {
        return this._cloneInto();
    }
}
/** Initial SHA512 state from RFC 6234 §6.3: eight RFC 64-bit `H(0)` words stored as sixteen
 * big-endian 32-bit halves. Derived from the fractional parts of the square roots of the first
 * eight prime numbers. Exported as a shared table; callers must treat it as read-only because
 * constructors copy halves from it by index. */
const SHA512_IV = /* @__PURE__ */ Uint32Array.from([
    0x6a09e667, 0xf3bcc908, 0xbb67ae85, 0x84caa73b, 0x3c6ef372, 0xfe94f82b, 0xa54ff53a, 0x5f1d36f1,
    0x510e527f, 0xade682d1, 0x9b05688c, 0x2b3e6c1f, 0x1f83d9ab, 0xfb41bd6b, 0x5be0cd19, 0x137e2179,
]);

/**
 * SHA2 hash function. A.k.a. sha256, sha384, sha512, sha512_224, sha512_256.
 * SHA256 is the fastest hash implementable in JS, even faster than Blake3.
 * Check out {@link https://www.rfc-editor.org/rfc/rfc4634 | RFC 4634} and
 * {@link https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf | FIPS 180-4}.
 * @module
 */
// SHA2-512 is slower than sha256 in js because u64 operations are slow.
// SHA-384 / SHA-512 round constants from RFC 6234 §5.2:
// 80 full 64-bit words split into high/low halves.
// prettier-ignore
const K512 = /* @__PURE__ */ (() => split([
    '0x428a2f98d728ae22', '0x7137449123ef65cd', '0xb5c0fbcfec4d3b2f', '0xe9b5dba58189dbbc',
    '0x3956c25bf348b538', '0x59f111f1b605d019', '0x923f82a4af194f9b', '0xab1c5ed5da6d8118',
    '0xd807aa98a3030242', '0x12835b0145706fbe', '0x243185be4ee4b28c', '0x550c7dc3d5ffb4e2',
    '0x72be5d74f27b896f', '0x80deb1fe3b1696b1', '0x9bdc06a725c71235', '0xc19bf174cf692694',
    '0xe49b69c19ef14ad2', '0xefbe4786384f25e3', '0x0fc19dc68b8cd5b5', '0x240ca1cc77ac9c65',
    '0x2de92c6f592b0275', '0x4a7484aa6ea6e483', '0x5cb0a9dcbd41fbd4', '0x76f988da831153b5',
    '0x983e5152ee66dfab', '0xa831c66d2db43210', '0xb00327c898fb213f', '0xbf597fc7beef0ee4',
    '0xc6e00bf33da88fc2', '0xd5a79147930aa725', '0x06ca6351e003826f', '0x142929670a0e6e70',
    '0x27b70a8546d22ffc', '0x2e1b21385c26c926', '0x4d2c6dfc5ac42aed', '0x53380d139d95b3df',
    '0x650a73548baf63de', '0x766a0abb3c77b2a8', '0x81c2c92e47edaee6', '0x92722c851482353b',
    '0xa2bfe8a14cf10364', '0xa81a664bbc423001', '0xc24b8b70d0f89791', '0xc76c51a30654be30',
    '0xd192e819d6ef5218', '0xd69906245565a910', '0xf40e35855771202a', '0x106aa07032bbd1b8',
    '0x19a4c116b8d2d0c8', '0x1e376c085141ab53', '0x2748774cdf8eeb99', '0x34b0bcb5e19b48a8',
    '0x391c0cb3c5c95a63', '0x4ed8aa4ae3418acb', '0x5b9cca4f7763e373', '0x682e6ff3d6b2b8a3',
    '0x748f82ee5defb2fc', '0x78a5636f43172f60', '0x84c87814a1f0ab72', '0x8cc702081a6439ec',
    '0x90befffa23631e28', '0xa4506cebde82bde9', '0xbef9a3f7b2c67915', '0xc67178f2e372532b',
    '0xca273eceea26619c', '0xd186b8c721c0c207', '0xeada7dd6cde0eb1e', '0xf57d4f7fee6ed178',
    '0x06f067aa72176fba', '0x0a637dc5a2c898a6', '0x113f9804bef90dae', '0x1b710b35131c471b',
    '0x28db77f523047d84', '0x32caab7b40c72493', '0x3c9ebe0a15c9bebc', '0x431d67c49c100d4c',
    '0x4cc5d4becb3e42b6', '0x597f299cfc657e2a', '0x5fcb6fab3ad6faec', '0x6c44198c4a475817'
].map(n => BigInt(n))))();
const SHA512_Kh = /* @__PURE__ */ (() => K512[0])();
const SHA512_Kl = /* @__PURE__ */ (() => K512[1])();
// Reusable high-half schedule buffer for the RFC 6234 §6.4 64-bit `W_t` words.
const SHA512_W_H = /* @__PURE__ */ new Uint32Array(80);
// Reusable low-half schedule buffer for the RFC 6234 §6.4 64-bit `W_t` words.
const SHA512_W_L = /* @__PURE__ */ new Uint32Array(80);
/** Internal SHA-384 / SHA-512 compression engine from RFC 6234 §6.4. */
class SHA2_64B extends HashMD {
    // We cannot use array here since array allows indexing by variable
    // which means optimizer/compiler cannot use registers.
    // h -- high 32 bits, l -- low 32 bits
    // Numeric initializers matter: starting the fields as `undefined` changes
    // V8's field representation and slows hashing down (measured on sha256).
    Ah = 0;
    Al = 0;
    Bh = 0;
    Bl = 0;
    Ch = 0;
    Cl = 0;
    Dh = 0;
    Dl = 0;
    Eh = 0;
    El = 0;
    Fh = 0;
    Fl = 0;
    Gh = 0;
    Gl = 0;
    Hh = 0;
    Hl = 0;
    constructor(outputLen, IV) {
        super(128, outputLen, 16, false);
        this.Ah = IV[0] | 0;
        this.Al = IV[1] | 0;
        this.Bh = IV[2] | 0;
        this.Bl = IV[3] | 0;
        this.Ch = IV[4] | 0;
        this.Cl = IV[5] | 0;
        this.Dh = IV[6] | 0;
        this.Dl = IV[7] | 0;
        this.Eh = IV[8] | 0;
        this.El = IV[9] | 0;
        this.Fh = IV[10] | 0;
        this.Fl = IV[11] | 0;
        this.Gh = IV[12] | 0;
        this.Gl = IV[13] | 0;
        this.Hh = IV[14] | 0;
        this.Hl = IV[15] | 0;
    }
    // prettier-ignore
    get() {
        const { Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl } = this;
        return [Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl];
    }
    // prettier-ignore
    set(Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl) {
        this.Ah = Ah | 0;
        this.Al = Al | 0;
        this.Bh = Bh | 0;
        this.Bl = Bl | 0;
        this.Ch = Ch | 0;
        this.Cl = Cl | 0;
        this.Dh = Dh | 0;
        this.Dl = Dl | 0;
        this.Eh = Eh | 0;
        this.El = El | 0;
        this.Fh = Fh | 0;
        this.Fl = Fl | 0;
        this.Gh = Gh | 0;
        this.Gl = Gl | 0;
        this.Hh = Hh | 0;
        this.Hl = Hl | 0;
    }
    _cloneInto(to) {
        (to ||= new this.constructor()).set(...this.get());
        return this._cloneIntoMeta(to);
    }
    process(view, offset) {
        // Extend the first 16 words into the remaining 64 words w[16..79] of the message schedule array
        for (let i = 0; i < 16; i++, offset += 4) {
            SHA512_W_H[i] = view.getUint32(offset);
            SHA512_W_L[i] = view.getUint32((offset += 4));
        }
        for (let i = 16; i < 80; i++) {
            // s0 := (w[i-15] rightrotate 1) xor (w[i-15] rightrotate 8) xor (w[i-15] rightshift 7)
            const W15h = SHA512_W_H[i - 15] | 0;
            const W15l = SHA512_W_L[i - 15] | 0;
            const s0h = rotrSH(W15h, W15l, 1) ^ rotrSH(W15h, W15l, 8) ^ shrSH(W15h, W15l, 7);
            const s0l = rotrSL(W15h, W15l, 1) ^ rotrSL(W15h, W15l, 8) ^ shrSL(W15h, W15l, 7);
            // s1 := (w[i-2] rightrotate 19) xor (w[i-2] rightrotate 61) xor (w[i-2] rightshift 6)
            const W2h = SHA512_W_H[i - 2] | 0;
            const W2l = SHA512_W_L[i - 2] | 0;
            const s1h = rotrSH(W2h, W2l, 19) ^ rotrBH(W2h, W2l, 61) ^ shrSH(W2h, W2l, 6);
            const s1l = rotrSL(W2h, W2l, 19) ^ rotrBL(W2h, W2l, 61) ^ shrSL(W2h, W2l, 6);
            // SHA512_W[i] = s0 + s1 + SHA512_W[i - 7] + SHA512_W[i - 16];
            const SUMl = add4L(s0l, s1l, SHA512_W_L[i - 7], SHA512_W_L[i - 16]);
            const SUMh = add4H(SUMl, s0h, s1h, SHA512_W_H[i - 7], SHA512_W_H[i - 16]);
            SHA512_W_H[i] = SUMh | 0;
            SHA512_W_L[i] = SUMl | 0;
        }
        let { Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl } = this;
        // Compression function main loop, 80 rounds
        for (let i = 0; i < 80; i++) {
            // S1 := (e rightrotate 14) xor (e rightrotate 18) xor (e rightrotate 41)
            const sigma1h = rotrSH(Eh, El, 14) ^ rotrSH(Eh, El, 18) ^ rotrBH(Eh, El, 41);
            const sigma1l = rotrSL(Eh, El, 14) ^ rotrSL(Eh, El, 18) ^ rotrBL(Eh, El, 41);
            //const T1 = (H + sigma1 + Chi(E, F, G) + SHA256_K[i] + SHA256_W[i]) | 0;
            const CHIh = (Eh & Fh) ^ (~Eh & Gh);
            const CHIl = (El & Fl) ^ (~El & Gl);
            // T1 = H + sigma1 + Chi(E, F, G) + SHA512_K[i] + SHA512_W[i]
            // prettier-ignore
            const T1ll = add5L(Hl, sigma1l, CHIl, SHA512_Kl[i], SHA512_W_L[i]);
            const T1h = add5H(T1ll, Hh, sigma1h, CHIh, SHA512_Kh[i], SHA512_W_H[i]);
            const T1l = T1ll | 0;
            // S0 := (a rightrotate 28) xor (a rightrotate 34) xor (a rightrotate 39)
            const sigma0h = rotrSH(Ah, Al, 28) ^ rotrBH(Ah, Al, 34) ^ rotrBH(Ah, Al, 39);
            const sigma0l = rotrSL(Ah, Al, 28) ^ rotrBL(Ah, Al, 34) ^ rotrBL(Ah, Al, 39);
            const MAJh = (Ah & Bh) ^ (Ah & Ch) ^ (Bh & Ch);
            const MAJl = (Al & Bl) ^ (Al & Cl) ^ (Bl & Cl);
            Hh = Gh | 0;
            Hl = Gl | 0;
            Gh = Fh | 0;
            Gl = Fl | 0;
            Fh = Eh | 0;
            Fl = El | 0;
            ({ h: Eh, l: El } = add(Dh | 0, Dl | 0, T1h | 0, T1l | 0));
            Dh = Ch | 0;
            Dl = Cl | 0;
            Ch = Bh | 0;
            Cl = Bl | 0;
            Bh = Ah | 0;
            Bl = Al | 0;
            const All = add3L(T1l, sigma0l, MAJl);
            Ah = add3H(All, T1h, sigma0h, MAJh);
            Al = All | 0;
        }
        // Add the compressed chunk to the current hash value
        ({ h: Ah, l: Al } = add(this.Ah | 0, this.Al | 0, Ah | 0, Al | 0));
        ({ h: Bh, l: Bl } = add(this.Bh | 0, this.Bl | 0, Bh | 0, Bl | 0));
        ({ h: Ch, l: Cl } = add(this.Ch | 0, this.Cl | 0, Ch | 0, Cl | 0));
        ({ h: Dh, l: Dl } = add(this.Dh | 0, this.Dl | 0, Dh | 0, Dl | 0));
        ({ h: Eh, l: El } = add(this.Eh | 0, this.El | 0, Eh | 0, El | 0));
        ({ h: Fh, l: Fl } = add(this.Fh | 0, this.Fl | 0, Fh | 0, Fl | 0));
        ({ h: Gh, l: Gl } = add(this.Gh | 0, this.Gl | 0, Gh | 0, Gl | 0));
        ({ h: Hh, l: Hl } = add(this.Hh | 0, this.Hl | 0, Hh | 0, Hl | 0));
        this.set(Ah, Al, Bh, Bl, Ch, Cl, Dh, Dl, Eh, El, Fh, Fl, Gh, Gl, Hh, Hl);
    }
    roundClean() {
        clean(SHA512_W_H, SHA512_W_L);
    }
    destroy() {
        // HashMD callers route post-destroy usability through `destroyed`; zeroizing alone still leaves
        // update()/digest() callable on reused instances.
        this.destroyed = true;
        clean(this.buffer);
        this.set(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
/** Internal SHA-512 hash class grounded in RFC 6234 §6.3 and §6.4. */
class _SHA512 extends SHA2_64B {
    constructor() {
        super(64, SHA512_IV);
    }
}
/**
 * SHA2-512 hash function from RFC 4634.
 * @param msg - message bytes to hash
 * @param opts - Reserved hash options.
 * @returns Digest bytes.
 * @example
 * Hash a message with SHA2-512.
 * ```ts
 * sha512(new Uint8Array([97, 98, 99]));
 * ```
 */
const sha512 = /* @__PURE__ */ createHasher(() => new _SHA512(), 
/* @__PURE__ */ oidNist(0x03));

/**
 * Hex, bytes and number utilities.
 * @module
 */
/*! noble-curves - MIT License (c) 2022 Paul Miller (paulmillr.com) */
/**
 * Asserts something is a plain object-ish value, not null or array.
 * @param value - Value to validate.
 * @param title - Label included in thrown errors.
 * @returns The validated object.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Validate an options object before checking fields.
 *
 * ```ts
 * aobject({ flag: true });
 * ```
 */
function aobject$1(value, title = 'object') {
    if (value === null || typeof value !== 'object' || Array.isArray(value))
        throw new TypeError(title === 'object'
            ? 'expected valid options object'
            : `"${title}" expected object, got type=${typeof value}`);
    return value;
}
// Shared error-message prefix builder. Only called on throw paths, so assert
// success paths never pay for the string concatenation.
const atitle = (title) => (title ? `"${title}" ` : '');
/**
 * Validates that a flag is boolean.
 * @param value - Value to validate.
 * @param title - Optional field name.
 * @returns Original value.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Reject non-boolean option flags early.
 *
 * ```ts
 * abool(true);
 * ```
 */
function abool(value, title = '') {
    if (typeof value !== 'boolean')
        throw new TypeError(atitle(title) + 'expected boolean, got type=' + typeof value);
    return value;
}
/**
 * Validates declared required and optional field types on a plain object.
 * Extra keys are intentionally ignored because many callers validate only the subset they use from
 * richer option bags or runtime objects.
 * This walks field schemas and formats detailed errors, so avoid it on hot paths; use direct
 * one-line guards such as `aobject()`, `afunction()`, `abool()`, or `asafenumber()` instead.
 * @param object - Object to validate.
 * @param fields - Required field types.
 * @param optFields - Optional field types.
 * @param title - Object label included in thrown errors.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Check user options before building a curve helper.
 *
 * ```ts
 * validateObject({ flag: true }, { flag: 'boolean' });
 * ```
 */
function validateObject(object, fields = {}, optFields = {}, title = 'object') {
    aobject$1(object, title);
    aobject$1(fields, 'fields');
    aobject$1(optFields, 'optFields');
    function checkField(fieldName, expectedType, isOpt) {
        const label = title === 'object' ? `param "${String(fieldName)}"` : `"${title}.${String(fieldName)}"`;
        // Config fields must be explicit own properties. Optional inherited values are rejected too
        // because callers keep reading the same options object after validation.
        const val = object[fieldName];
        // Runtime objects such as Field instances intentionally satisfy required method slots
        // via their shared prototype.
        if (!Object.hasOwn(object, fieldName) &&
            (isOpt ? val !== undefined : expectedType !== 'function')) {
            throw new TypeError(`${label} is invalid: expected own property`);
        }
        if (isOpt && val === undefined)
            return;
        const current = typeof val;
        if (current !== expectedType || val === null)
            throw new TypeError(`${label} is invalid: expected ${expectedType}, got ${current}`);
    }
    const iter = (f, isOpt) => Object.entries(f).forEach(([k, v]) => checkField(k, v, isOpt));
    iter(fields, false);
    iter(optFields, true);
}

/**
 * SHA3 (keccak) hash function, based on a new "Sponge function" design.
 * Different from older hashes, the internal state is bigger than output size.
 *
 * Check out
 * {@link https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.202.pdf | FIPS-202},
 * {@link https://keccak.team/keccak.html | Website}, and
 * {@link https://crypto.stackexchange.com/q/15727 | the differences between
 * SHA-3 and Keccak}.
 *
 * Check out `sha3-addons` module for cSHAKE, k12, and others.
 * @module
 */
// No __PURE__ annotations in sha3 header:
// EVERYTHING is in fact used on every export.
// Various per round constants calculations
const _0n = BigInt(0);
const _1n = BigInt(1);
const _2n = BigInt(2);
const _7n = BigInt(7);
const _256n = BigInt(256);
// FIPS 202 Algorithm 5 rc(): when the outgoing bit is 1, the 8-bit LFSR xors
// taps 0, 4, 5, and 6, which compresses to the feedback mask `0x71`.
const _0x71n = BigInt(0x71);
const SHA3_PI = [];
const SHA3_ROTL = [];
const _SHA3_IOTA = []; // no pure annotation: var is always used
for (let round = 0, R = _1n, x = 1, y = 0; round < 24; round++) {
    // Pi
    [x, y] = [y, (2 * x + 3 * y) % 5];
    SHA3_PI.push(2 * (5 * y + x));
    // Rotational
    SHA3_ROTL.push((((round + 1) * (round + 2)) / 2) % 64);
    // Iota
    let t = _0n;
    for (let j = 0; j < 7; j++) {
        R = ((R << _1n) ^ ((R >> _7n) * _0x71n)) % _256n;
        if (R & _2n)
            t ^= _1n << ((_1n << BigInt(j)) - _1n);
    }
    _SHA3_IOTA.push(t);
}
const IOTAS = split(_SHA3_IOTA, true);
// `split(..., true)` keeps the local little-endian lane-word layout used by
// `state32`, so these `H` / `L` tables follow the file's first-word /
// second-word lane slots rather than `_u64.ts`'s usual high/low naming.
const SHA3_IOTA_H = IOTAS[0];
const SHA3_IOTA_L = IOTAS[1];
// 64-bit left rotates as u32 pairs. Inlined here (not imported from _u64) so V8 can
// inline them into keccakP — the import path costs ~24% on sha3_256. SHA3 is the only
// consumer of left-rotates; other hashes use right-rotates from _u64.
// Valid for s in 1..31 (SH/SL) and 33..63 (BH/BL); keccak never rotates by 0/32/64.
const rotlSH = (h, l, s) => (h << s) | (l >>> (32 - s));
const rotlSL = (h, l, s) => (l << s) | (h >>> (32 - s));
const rotlBH = (h, l, s) => (l << (s - 32)) | (h >>> (64 - s));
const rotlBL = (h, l, s) => (h << (s - 32)) | (l >>> (64 - s));
const rotlH = (h, l, s) => (s > 32 ? rotlBH(h, l, s) : rotlSH(h, l, s));
const rotlL = (h, l, s) => (s > 32 ? rotlBL(h, l, s) : rotlSL(h, l, s));
// Reused Theta scratch buffer (column parities), same pattern as SHA256_W in sha2.
// keccakP never calls user code, so the shared buffer cannot be observed mid-permutation.
const B = new Uint32Array(5 * 2);
/**
 * `keccakf1600` internal permutation, additionally allows adjusting the round count.
 * @param s - 5x5 Keccak state encoded as 25 lanes split into 50 uint32 words
 *   in this file's local little-endian lane-word order
 * @param rounds - number of rounds to execute
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @throws If `rounds` is outside the supported `1..24` range. {@link Error}
 * @example
 * Permute a Keccak state with the default 24 rounds.
 * ```ts
 * keccakP(new Uint32Array(50));
 * ```
 */
function keccakP(s, rounds = 24) {
    if (!(s instanceof Uint32Array))
        throw new TypeError('"s" expected Uint32Array(50), got type=' + typeof s);
    if (s.length !== 50)
        throw new RangeError('"s" expected Uint32Array(50), got length=' + s.length);
    anumber(rounds, 'rounds');
    // This implementation precomputes only the standard Keccak-f[1600] 24-round Iota table.
    if (rounds < 1 || rounds > 24)
        throw new Error('"rounds" expected integer 1..24');
    // NOTE: all indices are x2 since we store state as u32 instead of u64 (bigints to slow in js)
    for (let round = 24 - rounds; round < 24; round++) {
        // Theta θ
        for (let x = 0; x < 10; x++)
            B[x] = s[x] ^ s[x + 10] ^ s[x + 20] ^ s[x + 30] ^ s[x + 40];
        for (let x = 0; x < 10; x += 2) {
            const idx1 = (x + 8) % 10;
            const idx0 = (x + 2) % 10;
            const B0 = B[idx0];
            const B1 = B[idx0 + 1];
            const Th = rotlH(B0, B1, 1) ^ B[idx1];
            const Tl = rotlL(B0, B1, 1) ^ B[idx1 + 1];
            for (let y = 0; y < 50; y += 10) {
                s[x + y] ^= Th;
                s[x + y + 1] ^= Tl;
            }
        }
        // Rho (ρ) and Pi (π)
        let curH = s[2];
        let curL = s[3];
        for (let t = 0; t < 24; t++) {
            const shift = SHA3_ROTL[t];
            const Th = rotlH(curH, curL, shift);
            const Tl = rotlL(curH, curL, shift);
            const PI = SHA3_PI[t];
            curH = s[PI];
            curL = s[PI + 1];
            s[PI] = Th;
            s[PI + 1] = Tl;
        }
        // Chi (χ)
        // Same as:
        // for (let x = 0; x < 10; x++) B[x] = s[y + x];
        // for (let x = 0; x < 10; x++) s[y + x] ^= ~B[(x + 2) % 10] & B[(x + 4) % 10];
        for (let y = 0; y < 50; y += 10) {
            const b0 = s[y], b1 = s[y + 1], b2 = s[y + 2], b3 = s[y + 3];
            s[y] ^= ~s[y + 2] & s[y + 4];
            s[y + 1] ^= ~s[y + 3] & s[y + 5];
            s[y + 2] ^= ~s[y + 4] & s[y + 6];
            s[y + 3] ^= ~s[y + 5] & s[y + 7];
            s[y + 4] ^= ~s[y + 6] & s[y + 8];
            s[y + 5] ^= ~s[y + 7] & s[y + 9];
            s[y + 6] ^= ~s[y + 8] & b0;
            s[y + 7] ^= ~s[y + 9] & b1;
            s[y + 8] ^= ~b0 & b2;
            s[y + 9] ^= ~b1 & b3;
        }
        // Iota (ι)
        s[0] ^= SHA3_IOTA_H[round];
        s[1] ^= SHA3_IOTA_L[round];
    }
    clean(B);
}
/**
 * Keccak sponge function.
 * @param blockLen - absorb/squeeze rate in bytes
 * @param suffix - domain separation suffix byte
 * @param outputLen - default digest length in bytes. This base sponge only
 *   requires a non-negative integer; wrappers that need positive output
 *   lengths must enforce that themselves.
 * @param enableXOF - whether XOF output is allowed
 * @param rounds - number of Keccak-f rounds
 * @example
 * Build a sponge state, absorb bytes, then finalize a digest.
 * ```ts
 * const hash = new Keccak(136, 0x06, 32);
 * hash.update(new Uint8Array([1, 2, 3]));
 * hash.digest();
 * ```
 */
class Keccak {
    state;
    pos = 0;
    posOut = 0;
    finished = false;
    state32;
    destroyed = false;
    blockLen;
    suffix;
    outputLen;
    canXOF;
    enableXOF = false;
    rounds;
    // NOTE: we accept arguments in bytes instead of bits here.
    constructor(blockLen, suffix, outputLen, enableXOF = false, rounds = 24) {
        anumber(blockLen, 'blockLen');
        anumber(suffix, 'suffix');
        anumber(rounds, 'rounds');
        abool$1(enableXOF, 'enableXOF');
        this.blockLen = blockLen;
        this.suffix = suffix;
        this.outputLen = outputLen;
        this.enableXOF = enableXOF;
        this.canXOF = enableXOF;
        this.rounds = rounds;
        // Can be passed from user as dkLen
        anumber(outputLen, 'outputLen');
        // Only keccak-f1600 is supported: 1600 bits (5x5 matrix of 64bit) === 200 bytes of state.
        if (!(0 < blockLen && blockLen < 200))
            throw new Error('"blockLen" must be 1..199');
        this.state = new Uint8Array(200);
        this.state32 = u32(this.state);
    }
    clone() {
        return this._cloneInto();
    }
    keccak() {
        swap32IfBE(this.state32);
        keccakP(this.state32, this.rounds);
        swap32IfBE(this.state32);
        this.posOut = 0;
        this.pos = 0;
    }
    update(data) {
        aexists(this);
        abytes(data);
        const { blockLen, state, state32 } = this;
        const len = data.length;
        // Absorb full blocks with u32 XORs when both sides are 4-byte aligned.
        // XOR of same-position words equals XOR of same-position bytes, so this is endianness-safe.
        const canUseU32 = blockLen % 4 === 0 && data.byteOffset % 4 === 0;
        const blockLen32 = blockLen / 4;
        const data32 = canUseU32 && len >= blockLen ? u32(data) : undefined;
        for (let pos = 0; pos < len;) {
            if (data32 !== undefined && this.pos === 0 && pos % 4 === 0 && len - pos >= blockLen) {
                for (let i = 0, o = pos / 4; i < blockLen32; i++)
                    state32[i] ^= data32[o + i];
                pos += blockLen;
                // Subclasses (_KeccakPRG) read `this.pos` inside their `keccak()` override,
                // so it must reflect the fully-absorbed block before the permutation fires.
                this.pos = blockLen;
                this.keccak();
                continue;
            }
            const take = Math.min(blockLen - this.pos, len - pos);
            for (let i = 0; i < take; i++)
                state[this.pos++] ^= data[pos++];
            if (this.pos === blockLen)
                this.keccak();
        }
        return this;
    }
    finish() {
        if (this.finished)
            return;
        this.finished = true;
        const { state, suffix, pos, blockLen } = this;
        // FIPS 202 appends the SHA3/SHAKE domain-separation suffix before pad10*1.
        // These byte values already include the first padding bit, while the
        // final `0x80` below supplies the closing `1` bit in the last rate byte.
        state[pos] ^= suffix;
        // If that combined suffix lands in the last rate byte and already sets
        // bit 7, absorb it first so the final pad10*1 bit can be xored into a
        // fresh block.
        if ((suffix & 0x80) !== 0 && pos === blockLen - 1)
            this.keccak();
        state[blockLen - 1] ^= 0x80;
        this.keccak();
    }
    writeInto(out) {
        aexists(this, false);
        abytes(out);
        this.finish();
        const bufferOut = this.state;
        const { blockLen } = this;
        for (let pos = 0, len = out.length; pos < len;) {
            if (this.posOut >= blockLen)
                this.keccak();
            const take = Math.min(blockLen - this.posOut, len - pos);
            out.set(bufferOut.subarray(this.posOut, this.posOut + take), pos);
            this.posOut += take;
            pos += take;
        }
        return out;
    }
    xofInto(out) {
        // Plain SHA3/Keccak usage with XOF is probably a mistake, but this base
        // class is also reused by SHAKE/cSHAKE/KMAC/TupleHash/ParallelHash/
        // TurboSHAKE/KangarooTwelve wrappers that intentionally enable XOF.
        if (!this.enableXOF)
            throw new Error('XOF is not enabled');
        return this.writeInto(out);
    }
    xof(bytes) {
        anumber(bytes);
        return this.xofInto(new Uint8Array(bytes));
    }
    digestInto(out) {
        aoutput(out, this);
        if (this.finished)
            throw new Error('digest() was already called');
        // `aoutput(...)` allows oversized buffers; digestInto() must fill only the advertised digest.
        this.writeInto(out.length === this.outputLen ? out : out.subarray(0, this.outputLen));
        this.destroy();
    }
    digest() {
        const out = new Uint8Array(this.outputLen);
        this.digestInto(out);
        return out;
    }
    destroy() {
        this.destroyed = true;
        clean(this.state);
    }
    _cloneInto(to) {
        const { blockLen, suffix, outputLen, rounds, enableXOF } = this;
        to ||= new Keccak(blockLen, suffix, outputLen, enableXOF, rounds);
        // Reused destinations can come from a different rate/capacity variant, so clone must rewrite
        // the sponge geometry as well as the state words.
        to.blockLen = blockLen;
        to.state32.set(this.state32);
        // Sponge padding and XOF output are positional, so both offsets are part of the clone state.
        to.pos = this.pos;
        to.posOut = this.posOut;
        to.finished = this.finished;
        to.rounds = rounds;
        // Suffix can change in cSHAKE
        to.suffix = suffix;
        to.outputLen = outputLen;
        to.enableXOF = enableXOF;
        // Clones must preserve the public capability bit too; `_KMAC` reuses this path and deep clone
        // tests compare instance fields directly, so leaving `canXOF` behind makes the clone lie.
        to.canXOF = this.canXOF;
        to.destroyed = this.destroyed;
        return to;
    }
}
const genShake = (suffix, blockLen, outputLen, info = {}) => createHasher((opts = {}) => {
    opts = checkOpts({}, opts);
    return new Keccak(blockLen, suffix, opts.dkLen === undefined ? outputLen : opts.dkLen, true);
}, info);
/**
 * SHAKE128 XOF with 128-bit security and a 16-byte default output.
 * @param msg - message bytes to hash
 * @param opts - Optional output-length override. See {@link ShakeOpts}.
 * @returns Digest bytes.
 * @example
 * Hash a message with SHAKE128.
 * ```ts
 * shake128(new Uint8Array([97, 98, 99]), { dkLen: 32 });
 * ```
 */
const shake128 = 
/* @__PURE__ */
genShake(0x1f, 168, 16, /* @__PURE__ */ oidNist(0x0b));
/**
 * SHAKE256 XOF with 256-bit security and a 32-byte default output.
 * @param msg - message bytes to hash
 * @param opts - Optional output-length override. See {@link ShakeOpts}.
 * @returns Digest bytes.
 * @example
 * Hash a message with SHAKE256.
 * ```ts
 * shake256(new Uint8Array([97, 98, 99]), { dkLen: 64 });
 * ```
 */
const shake256 = 
/* @__PURE__ */
genShake(0x1f, 136, 32, /* @__PURE__ */ oidNist(0x0c));

/**
 * Experimental implementation of NTT / FFT (Fast Fourier Transform) over finite fields.
 * API may change at any time. The code has not been audited. Feature requests are welcome.
 * @module
 */
function checkU32(n, title = 'n') {
    // 0xff_ff_ff_ff
    if (typeof n !== 'number')
        throw new TypeError(`wrong u32 integer "${title}": expected number, got type=${typeof n}`);
    if (!Number.isSafeInteger(n) || n < 0 || n > 0xffffffff)
        throw new RangeError(`wrong u32 integer "${title}": expected 0..4294967295, got ${n}`);
    return n;
}
/**
 * Checks if integer is in form of `1 << X`.
 * @param x - Integer to inspect.
 * @returns `true` when the value is a power of two.
 * @example
 * Validate that an FFT size is a power of two.
 *
 * ```ts
 * isPowerOfTwo(8);
 * ```
 */
function isPowerOfTwo(x) {
    checkU32(x, 'x');
    return (x & (x - 1)) === 0 && x !== 0;
}
/**
 * @param n - Value to reverse.
 * @param bits - Number of bits to use.
 * @returns Bit-reversed integer.
 * @throws If `n` is not a valid unsigned 32-bit integer. {@link Error}
 * @example
 * Reverse the low `bits` bits of one index.
 *
 * ```ts
 * reverseBits(3, 3);
 * ```
 */
function reverseBits(n, bits) {
    checkU32(n);
    if (typeof bits !== 'number')
        throw new TypeError('"bits" expected number, got type=' + typeof bits);
    if (!Number.isSafeInteger(bits) || bits < 0 || bits > 32)
        throw new Error(`expected integer 0 <= bits <= 32, got ${bits}`);
    let reversed = 0;
    for (let i = 0; i < bits; i++, n >>>= 1)
        reversed = (reversed << 1) | (n & 1);
    // JS bitwise ops are signed i32; cast back so 32-bit reversals stay in the unsigned u32 domain.
    return reversed >>> 0;
}
/**
 * Similar to `bitLen(x)-1` but much faster for small integers, like indices.
 * @param n - Input value.
 * @returns Base-2 logarithm. For `n = 0`, the current implementation returns `-1`.
 * @example
 * Compute the radix-2 stage count for one transform size.
 *
 * ```ts
 * log2(8);
 * ```
 */
function log2(n) {
    checkU32(n);
    return 31 - Math.clz32(n);
}
/**
 * Moves lowest bit to highest position, which at first step splits
 * array on even and odd indices, then it applied again to each part,
 * which is core of fft
 * @param values - Mutable coefficient array.
 * @returns Mutated input array.
 * @throws If the array length is not a positive power of two. {@link Error}
 * @example
 * Reorder coefficients into bit-reversed order in place.
 *
 * ```ts
 * const values = Uint8Array.from([0, 1, 2, 3]);
 * bitReversalInplace(values);
 * ```
 */
function bitReversalInplace(values) {
    if (!values ||
        typeof values !== 'object' ||
        typeof values.length !== 'number')
        throw new TypeError('"values" expected array-like, got type=' + typeof values);
    const n = values.length;
    // Size-1 FFT is the identity, so bit-reversal must stay a no-op there instead of rejecting it.
    if (!isPowerOfTwo(n))
        throw new Error('expected positive power-of-two length, got ' + n);
    const bits = log2(n);
    for (let i = 0; i < n; i++) {
        const j = reverseBits(i, bits);
        if (i < j) {
            const tmp = values[i];
            values[i] = values[j];
            values[j] = tmp;
        }
    }
    return values;
}
/**
 * Constructs different flavors of FFT. radix2 implementation of low level mutating API. Flavors:
 *
 * - DIT (Decimation-in-Time): Bottom-Up (leaves to root), Cooley-Tukey
 * - DIF (Decimation-in-Frequency): Top-Down (root to leaves), Gentleman-Sande
 *
 * DIT takes brp input, returns natural output.
 * DIF takes natural input, returns brp output.
 *
 * The output is actually identical. Time / frequence distinction is not meaningful
 * for Polynomial multiplication in fields.
 * Which means if protocol supports/needs brp output/inputs, then we can skip this step.
 *
 * Cyclic NTT: Rq = Zq[x]/(x^n-1). butterfly_DIT+loop_DIT OR butterfly_DIF+loop_DIT, roots are omega
 * Negacyclic NTT: Rq = Zq[x]/(x^n+1). butterfly_DIT+loop_DIF, at least for mlkem / mldsa
 *
 * `invertButterflies` indexes roots by a per-butterfly-group counter (`grp`): forward
 * (`dit: false`) reads `roots[grp]` with grp = 1..; inverse (`dit: true`) reads `roots[N - grp]`
 * with grp restarting at 1. With `skipStages: 0` one table serves both directions (ωᴺ = 1 makes
 * the reversed walk self-inverse). With `skipStages > 0` the inverse walk starts at `N - 1`
 * instead of continuing where the skipped stages would have left off, so the caller must supply
 * a table shaped for that (ML-KEM: `ζ^BitRev7(i)` over all N=256 indices, whose aliased upper
 * half is exactly the FIPS 203 inverse walk).
 * @param F - Field operations.
 * @param coreOpts - FFT configuration. See {@link FFTCoreOpts}:
 *   - `N`: Transform size. Must be a power of two.
 *   - `roots`: Stage roots for the selected transform size.
 *   - `dit`: Whether to run the DIT variant instead of DIF.
 *   - `invertButterflies` (optional): Whether to invert butterfly placement.
 *   - `skipStages` (optional): Number of initial stages to skip.
 *   - `brp` (optional): Whether to apply bit-reversal permutation at the boundary.
 * @returns Low-level FFT loop.
 * @throws If the FFT options or cached roots are invalid for the requested size. {@link Error}
 * @example
 * Constructs different flavors of FFT.
 *
 * ```ts
 * import { FFTCore, rootsOfUnity } from '@noble/curves/abstract/fft.js';
 * import { Field } from '@noble/curves/abstract/modular.js';
 * const Fp = Field(17n);
 * const roots = rootsOfUnity(Fp).roots(2);
 * const loop = FFTCore(Fp, { N: 4, roots, dit: true });
 * const values = loop([1n, 2n, 3n, 4n]);
 * ```
 */
const FFTCore = (F, coreOpts) => {
    validateObject(coreOpts, { N: 'number', roots: 'object', dit: 'boolean' }, { invertButterflies: 'boolean', skipStages: 'number', brp: 'boolean' }, 'coreOpts');
    const { N, roots, dit, invertButterflies = false, skipStages = 0, brp = true } = coreOpts;
    checkU32(N, 'coreOpts.N');
    const bits = log2(N);
    if (!isPowerOfTwo(N))
        throw new Error('FFT: Polynomial size should be power of two');
    checkU32(skipStages, 'coreOpts.skipStages');
    const maxSkipStages = bits === 0 ? 0 : bits - 1;
    // Skipping every stage leaves only boundary layout changes, not a valid FFT loop shape.
    if (skipStages > maxSkipStages)
        throw new Error(`FFT: wrong skipStages: expected 0 <= skipStages <= ${maxSkipStages}`);
    // Wrong-sized root tables can stay in-bounds for some loop shapes and silently compute nonsense.
    if (roots.length !== N)
        throw new Error(`FFT: wrong roots length: expected ${N}, got ${roots.length}`);
    const isDit = dit !== invertButterflies;
    return (values) => {
        if (values.length !== N)
            throw new Error('FFT: wrong Polynomial length');
        if (dit && brp)
            bitReversalInplace(values);
        for (let i = 0, g = 1; i < bits - skipStages; i++) {
            // For each stage s (sub-FFT length m = 2^s)
            const s = dit ? i + 1 + skipStages : bits - i;
            const m = 1 << s;
            const m2 = m >> 1;
            const stride = N >> s;
            // Loop over each subarray of length m
            for (let k = 0; k < N; k += m) {
                // Loop over each butterfly within the subarray
                for (let j = 0, grp = g++; j < m2; j++) {
                    const rootPos = invertButterflies ? (dit ? N - grp : grp) : j * stride;
                    const i0 = k + j;
                    const i1 = k + j + m2;
                    const omega = roots[rootPos];
                    const b = values[i1];
                    const a = values[i0];
                    // Inlining gives us 10% perf in kyber vs functions
                    if (isDit) {
                        const t = F.mul(b, omega); // Standard DIT butterfly
                        values[i0] = F.add(a, t);
                        values[i1] = F.sub(a, t);
                    }
                    else if (invertButterflies) {
                        values[i0] = F.add(b, a); // DIT loop + inverted butterflies (Kyber decode)
                        values[i1] = F.mul(F.sub(b, a), omega);
                    }
                    else {
                        values[i0] = F.add(a, b); // Standard DIF butterfly
                        values[i1] = F.mul(F.sub(a, b), omega);
                    }
                }
            }
        }
        if (!dit && brp)
            bitReversalInplace(values);
        return values;
    };
};

/**
 * Utilities for hex, bytearray and number handling.
 * @module
 */
/*! noble-post-quantum - MIT License (c) 2024 Paul Miller (paulmillr.com) */
/**
 * Asserts that a value is a byte array and optionally checks its length.
 * Returns the original reference unchanged on success, and currently also accepts Node `Buffer`
 * values through the upstream validator.
 * This helper throws on malformed input, so APIs that must return `false` need to guard lengths
 * before decoding or before calling it.
 * @example
 * Validate that a value is a byte array with the expected length.
 * ```ts
 * abytes(new Uint8Array([1]), 1);
 * ```
 */
const abytesDoc = abytes;
/**
 * Returns cryptographically secure random bytes.
 * Requires `globalThis.crypto.getRandomValues` and throws if that API is unavailable.
 * `bytesLength` is validated by the upstream helper as a non-negative integer before allocation,
 * so negative and fractional values both throw instead of truncating through JS `ToIndex`.
 * @param bytesLength - Number of random bytes to generate.
 * @returns Fresh random bytes.
 * @example
 * Generate a fresh random seed.
 * ```ts
 * const seed = randomBytes(4);
 * ```
 */
const randomBytes = randomBytes$1;
function aarray(item, title, inner = () => { }) {
    if (!Array.isArray(item))
        throw new TypeError(`"${title}" expected array, got type=${typeof item}`);
    for (let i = 0; i < item.length; i++)
        inner(item[i], `${title}[${i}]`);
    return item;
}
function aobject(value, title = 'object') {
    if (value === null || typeof value !== 'object' || Array.isArray(value))
        throw new TypeError(title === 'object'
            ? 'expected valid options object'
            : `"${title}" expected object, got type=${typeof value}`);
    return value;
}
/**
 * Compares two byte arrays in a length-constant way for equal lengths.
 * Inputs are validated as byte arrays; unequal lengths return `false` immediately.
 * @param a - First byte array.
 * @param b - Second byte array.
 * @returns Whether both arrays contain the same bytes.
 * @example
 * Compare two byte arrays for equality.
 * ```ts
 * equalBytes(new Uint8Array([1]), new Uint8Array([1]));
 * ```
 */
function equalBytes(a, b) {
    a = abytes(a);
    b = abytes(b);
    if (a.length !== b.length)
        return false;
    let diff = 0;
    for (let i = 0; i < a.length; i++)
        diff |= a[i] ^ b[i];
    return diff === 0;
}
/**
 * Validates that an options bag is a plain object.
 * @param opts - Options object to validate.
 * @throws On wrong argument types. {@link TypeError}
 * @example
 * Validate that an options bag is a plain object.
 * ```ts
 * validateOpts({});
 * ```
 */
function validateOpts(opts) {
    // Arrays silently passed here before, but these call sites expect named option-bag fields.
    if (isBytes(opts))
        throw new TypeError('"opts" expected object, got Uint8Array');
    aobject(opts, 'opts');
    const proto = Object.getPrototypeOf(opts);
    // Options are security parameters, not general class instances. Restricting the bag to own
    // properties prevents values injected through Object.prototype (or a custom shared prototype)
    // from silently changing signing behavior. Null-prototype records remain supported.
    if (proto !== null && proto !== Object.prototype)
        throw new TypeError('"opts" expected a plain object');
}
// Frozen because they are exported: an unfrozen array export lets anything in the
// process push a key onto the accepted set and silently re-open exactly the hole this
// validation closes.
/** Keys accepted by `verify`. */
const VER_OPT_KEYS = /* @__PURE__ */ Object.freeze([
    'context',
]);
/** Keys accepted by `sign`. */
const SIG_OPT_KEYS = /* @__PURE__ */ Object.freeze([
    'context',
    'extraEntropy',
]);
/**
 * Rejects option keys the caller did not mean to set.
 *
 * Validating the types of known keys while ignoring unknown ones makes a typo
 * indistinguishable from an omission, and for these options an omission is a
 * security downgrade rather than a no-op: `{ ctx }` instead of `{ context }` signs
 * with no domain separation, succeeds, and verifies for anyone who also supplies
 * none. Nothing at any layer reports it. TypeScript catches this through excess
 * property checks, so the exposure is JavaScript callers specifically.
 *
 * @param opts - Options object to check.
 * @param allowed - The keys this call site accepts.
 * Returns a frozen null-prototype snapshot so later reads cannot fall through to a polluted
 * prototype. Like `checkOpts()` in noble-hashes, only enumerable own properties are copied.
 * @throws If any other copied key is present or the bag has a custom prototype. {@link TypeError}
 * @returns Sanitized snapshot of the enumerable own options.
 * @example
 * Accept a known option key. A key the list does not name, such as `ctx`, throws instead.
 * ```ts
 * import { checkOptKeys } from '@noble/post-quantum/utils.js';
 * checkOptKeys({ context: new Uint8Array() }, ['context']);
 * ```
 */
function checkOptKeys(opts, allowed) {
    validateOpts(opts);
    // Snapshot once before validation: Object.assign follows the same own-enumerable option-bag
    // semantics as noble-hashes, while the null prototype keeps omitted fields immune to pollution.
    const normalized = Object.assign(Object.create(null), opts);
    for (const [k, v] of Object.entries(normalized)) {
        // `undefined` means unset everywhere else in these validators, and building an options bag by
        // spread is a normal way to reach these calls, so present-but-undefined stays equivalent to
        // omission.
        if (v === undefined)
            continue;
        if (!allowed.includes(k))
            throw new TypeError('unexpected option "' + String(k) + '"; expected one of: ' + allowed.join(', '));
    }
    return Object.freeze(normalized);
}
/**
 * Validates common verification options.
 * `context` itself is validated with `abytes(...)`, and individual algorithms may narrow support
 * further after this shared plain-object gate.
 * @param opts - Verification options. See {@link VerOpts}.
 * @param allowed - Keys this call site accepts. Defaults to {@link VER_OPT_KEYS}; surfaces that
 * take extra keys, or take fewer, pass their own list.
 * @throws On wrong argument types. {@link TypeError}
 * @returns Frozen null-prototype snapshot of the validated options.
 * @example
 * Validate common verification options.
 * ```ts
 * validateVerOpts({ context: new Uint8Array([1]) });
 * ```
 */
function validateVerOpts(opts, allowed = VER_OPT_KEYS) {
    const normalized = checkOptKeys(opts, allowed);
    if (normalized.context !== undefined)
        abytes(normalized.context, undefined, 'opts.context');
    return normalized;
}
/**
 * Validates common signing options.
 * `extraEntropy` is validated with `abytes(...)`; exact lengths and extra algorithm-specific
 * restrictions are enforced later by callers.
 * @param opts - Signing options. See {@link SigOpts}.
 * @param allowed - Keys this call site accepts. Defaults to {@link SIG_OPT_KEYS}; surfaces that
 * take extra keys, or take fewer, pass their own list.
 * @throws On wrong argument types. {@link TypeError}
 * @returns Frozen null-prototype snapshot of the validated options.
 * @example
 * Validate common signing options.
 * ```ts
 * validateSigOpts({ extraEntropy: new Uint8Array([1]) });
 * ```
 */
function validateSigOpts(opts, allowed = SIG_OPT_KEYS) {
    const normalized = checkOptKeys(opts, allowed);
    if (normalized.context !== undefined)
        abytes(normalized.context, undefined, 'opts.context');
    if (normalized.extraEntropy !== false && normalized.extraEntropy !== undefined)
        abytes(normalized.extraEntropy, undefined, 'opts.extraEntropy');
    return normalized;
}
/**
 * Builds a fixed-layout coder from byte lengths and nested coders.
 * Raw-length fields decode as zero-copy `subarray(...)` views, and nested coders may preserve that
 * aliasing too. Nested coder `encode(...)` results are treated as owned scratch: `splitCoder`
 * copies them into the output and then zeroizes them with `fill(0)`. If a nested encoder forwards
 * caller-owned bytes, it must do so only after detaching them into a disposable copy.
 * @param label - Label used in validation errors.
 * @param lengths - Field lengths or nested coders.
 * @returns Composite fixed-length coder.
 * @example
 * Build a fixed-layout coder from byte lengths and nested coders.
 * ```ts
 * splitCoder('demo', 1, 2).encode([new Uint8Array([1]), new Uint8Array([2, 3])]);
 * ```
 */
function splitCoder(label, ...lengths) {
    const getLength = (c) => typeof c === 'number' ? c : c.bytesLen;
    const bytesLen = lengths.reduce((sum, a) => sum + getLength(a), 0);
    return {
        bytesLen,
        encode: (bufs) => {
            const res = new Uint8Array(bytesLen);
            for (let i = 0, pos = 0; i < lengths.length; i++) {
                const c = lengths[i];
                const l = getLength(c);
                const b = typeof c === 'number' ? bufs[i] : c.encode(bufs[i]);
                abytes(b, l, label);
                res.set(b, pos);
                if (typeof c !== 'number')
                    b.fill(0); // clean
                pos += l;
            }
            return res;
        },
        decode: (buf) => {
            abytes(buf, bytesLen, label);
            const res = [];
            for (const c of lengths) {
                const l = getLength(c);
                const b = buf.subarray(0, l);
                res.push(typeof c === 'number' ? b : c.decode(b));
                buf = buf.subarray(l);
            }
            return res;
        },
    };
}
// nano-packed.array (fixed size)
/**
 * Builds a fixed-length vector coder from another fixed-length coder.
 * Element decoding receives `subarray(...)` views, so aliasing depends on the element coder.
 * Element coder `encode(...)` results are treated as owned scratch: `vecCoder` copies them into
 * the output and then zeroizes them with `fill(0)`. If an element encoder forwards caller-owned
 * bytes, it must do so only after detaching them into a disposable copy. `vecCoder` also trusts
 * the `BytesCoderLen` contract: each encoded element must already be exactly `c.bytesLen` bytes.
 * @param c - Element coder.
 * @param vecLen - Number of elements in the vector.
 * @returns Fixed-length vector coder.
 * @example
 * Build a fixed-length vector coder from another fixed-length coder.
 * ```ts
 * vecCoder(
 *   { bytesLen: 1, encode: (n: number) => Uint8Array.of(n), decode: (b: Uint8Array) => b[0] || 0 },
 *   2
 * ).encode([1, 2]);
 * ```
 */
function vecCoder(c, vecLen) {
    const coder = c;
    const bytesLen = vecLen * coder.bytesLen;
    return {
        bytesLen,
        encode: (u) => {
            const uArr = aarray(u, 'u');
            if (uArr.length !== vecLen)
                throw new RangeError(`vecCoder.encode: wrong length=${uArr.length}. Expected: ${vecLen}`);
            const res = new Uint8Array(bytesLen);
            for (let i = 0, pos = 0; i < uArr.length; i++) {
                const b = coder.encode(uArr[i]);
                res.set(b, pos);
                b.fill(0); // clean
                pos += b.length;
            }
            return res;
        },
        decode: (a) => {
            abytes(a, bytesLen);
            const r = [];
            for (let i = 0; i < a.length; i += coder.bytesLen)
                r.push(coder.decode(a.subarray(i, i + coder.bytesLen)));
            return r;
        },
    };
}
/**
 * Overwrites supported typed-array inputs with zeroes in place.
 * Accepts direct typed arrays and one-level arrays of them.
 * @param list - Typed arrays or one-level lists of typed arrays to clear.
 * @example
 * Overwrite typed arrays with zeroes.
 * ```ts
 * const buf = Uint8Array.of(1, 2, 3);
 * cleanBytes(buf);
 * ```
 */
function cleanBytes(...list) {
    for (const t of list) {
        if (Array.isArray(t))
            for (const b of t)
                b.fill(0);
        else
            t.fill(0);
    }
}
/**
 * Creates a 32-bit mask with the lowest `bits` bits set.
 * @param bits - Number of low bits to keep.
 * @returns Bit mask with `bits` ones.
 * @throws On wrong argument types. {@link TypeError}
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Create a low-bit mask for packed-field operations.
 * ```ts
 * const mask = getMask(4);
 * ```
 */
function getMask(bits) {
    anumber(bits, 'bits');
    if (bits > 32)
        throw new RangeError('"bits" expected <= 32, got ' + bits);
    // JS shifts are modulo 32, so bit 32 needs an explicit full-width mask.
    return bits === 32 ? 0xffffffff : ~(-1 << bits) >>> 0;
}
/** Shared empty byte array used as the default context. */
const EMPTY = /* @__PURE__ */ Uint8Array.of();
/**
 * Builds the domain-separated message payload for the pure sign/verify paths.
 * Context length `255` is valid; only `ctx.length > 255` is rejected.
 * @param msg - Message bytes.
 * @param ctx - Optional context bytes.
 * @returns Domain-separated message payload.
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Build the domain-separated payload before direct signing.
 * ```ts
 * const payload = getMessage(new Uint8Array([1, 2]));
 * ```
 */
function getMessage(msg, ctx = EMPTY) {
    abytes(msg, undefined, 'msg');
    abytes(ctx, undefined, 'ctx');
    if (ctx.length > 255)
        throw new RangeError('context should be 255 bytes or less');
    return concatBytes(new Uint8Array([0, ctx.length]), ctx, msg);
}
// DER tag+length plus the shared NIST hash OID arc 2.16.840.1.101.3.4.2.* used by the
// FIPS 204 / FIPS 205 pre-hash wrappers; the final byte selects SHA-256, SHA-512, SHAKE128,
// SHAKE256, or another approved hash/XOF under that subtree.
// 06 09 60 86 48 01 65 03 04 02
const oidNistP = /* @__PURE__ */ Uint8Array.from([6, 9, 0x60, 0x86, 0x48, 1, 0x65, 3, 4, 2]);
/**
 * Output length, in bytes, that each XOF OID under this arc denotes.
 *
 * Unlike a fixed hash, an XOF's OID is a promise about the digest length: RFC 8702
 * defines id-shake128 as SHAKE128 with 256-bit output and id-shake256 as SHAKE256 with
 * 512-bit output, and FIPS 204 / FIPS 205 use exactly those pairings for pre-hash. Both
 * bare noble-hashes defaults are half these values, so neither can be signed under its
 * own OID.
 */
const XOF_OID_OUTPUT_LEN = /* @__PURE__ */ (() => ({
    '060960864801650304020b': 32, // id-shake128, SHAKE128(M, 256)
    '060960864801650304020c': 64, // id-shake256, SHAKE256(M, 512)
}))();
/**
 * Validates that a hash exposes a NIST hash OID and enough collision resistance.
 * Current accepted surface is broader than the FIPS algorithm tables: any hash/XOF under the NIST
 * `2.16.840.1.101.3.4.2.*` subtree is accepted if its effective `outputLen` is strong enough.
 * XOF callers must pass a callable whose `outputLen` matches the digest length they actually intend
 * to sign; bare `shake128` / `shake256` defaults are too short for the stronger prehash modes.
 * @param hash - Hash function to validate.
 * @param requiredStrength - Minimum required collision-resistance strength in bits.
 * @throws If the hash metadata or collision resistance is insufficient. {@link Error}
 * @example
 * Validate that a hash exposes a NIST hash OID and enough collision resistance.
 * ```ts
 * import { sha256 } from '@noble/hashes/sha2.js';
 * import { checkHash } from '@noble/post-quantum/utils.js';
 * checkHash(sha256, 128);
 * ```
 */
function checkHash(hash, requiredStrength = 0) {
    if (typeof hash !== 'function' || typeof hash.create !== 'function')
        throw new TypeError('"hash" expected hash function, got type=' + typeof hash);
    ahash(hash);
    anumber(requiredStrength, 'requiredStrength');
    const oid = hash.oid;
    abytes(oid, undefined, 'hash.oid');
    if (!equalBytes(oid.subarray(0, 10), oidNistP))
        throw new Error('"hash.oid" is invalid: expected NIST hash');
    // FIPS 204 / FIPS 205 require both collision and second-preimage strength; for approved NIST
    // hashes/XOFs under this OID subtree, the collision bound from the configured digest length is
    // the tighter runtime check, so enforce that lower bound here.
    // XOFs under this arc are identified by an OID that fixes their output length:
    // FIPS 204 §5.4.1 (SHAKE128) and FIPS 205 §10.2.2 (both SHAKEs), matching RFC 8702, pair
    // id-shake128 with SHAKE128(M, 256) and id-shake256 with SHAKE256(M, 512). getMessagePrehash embeds
    // hash.oid beside hash(msg), so a shorter digest signs an M' that claims a length
    // it does not have: noble-hashes' bare shake256 defaults to 32 bytes and cleared
    // the collision bound at the 128-bit level, producing signatures a conformant
    // verifier rejects because it recomputes 512 bits. Check the length the OID
    // denotes rather than the generic bound.
    const xofLen = XOF_OID_OUTPUT_LEN[bytesToHex(oid)];
    if (xofLen !== undefined && hash.outputLen !== xofLen) {
        throw new Error('Pre-hash XOF output length must be ' + xofLen + ' bytes for this OID, got: ' + hash.outputLen);
    }
    const collisionResistance = (hash.outputLen * 8) / 2;
    if (requiredStrength > collisionResistance) {
        throw new Error('Pre-hash security strength too low: ' +
            collisionResistance +
            ', required: ' +
            requiredStrength);
    }
}
/**
 * Builds the domain-separated prehash payload for the prehash sign/verify paths.
 * Callers are expected to vet `hash.oid` first, e.g. via `checkHash(...)`; calling this helper
 * directly with a hash object that lacks `oid` currently throws later inside `concatBytes(...)`.
 * Context length `255` is valid; only `ctx.length > 255` is rejected.
 * @param hash - Prehash function.
 * @param msg - Message bytes.
 * @param ctx - Optional context bytes.
 * @returns Domain-separated prehash payload.
 * @throws On wrong argument ranges or values. {@link RangeError}
 * @example
 * Build the domain-separated prehash payload for external hashing.
 * ```ts
 * import { sha256 } from '@noble/hashes/sha2.js';
 * import { getMessagePrehash } from '@noble/post-quantum/utils.js';
 * getMessagePrehash(sha256, new Uint8Array([1, 2]));
 * ```
 */
function getMessagePrehash(hash, msg, ctx = EMPTY) {
    checkHash(hash);
    abytes(msg, undefined, 'msg');
    abytes(ctx, undefined, 'ctx');
    if (ctx.length > 255)
        throw new RangeError('context should be 255 bytes or less');
    const hashed = hash(msg);
    return concatBytes(new Uint8Array([1, ctx.length]), ctx, hash.oid, hashed);
}

/**
 * Internal methods for lattice-based ML-KEM and ML-DSA.
 * @module
 */
/*! noble-post-quantum - MIT License (c) 2024 Paul Miller (paulmillr.com) */
/**
 * Creates shared modular arithmetic, NTT, and packing helpers for CRYSTALS schemes.
 * @param opts - Polynomial and transform parameters. See {@link CrystalOpts}.
 * @returns CRYSTALS arithmetic and encoding helpers.
 * @example
 * Create shared modular arithmetic and NTT helpers for a CRYSTALS parameter set.
 * ```ts
 * const crystals = genCrystals({
 *   newPoly: (n) => new Uint16Array(n),
 *   N: 256,
 *   Q: 3329,
 *   F: 3303,
 *   ROOT_OF_UNITY: 17,
 *   brvBits: 7,
 *   isKyber: true,
 * });
 * const reduced = crystals.mod(-1);
 * ```
 */
const genCrystals = (opts) => {
    // isKyber: true means Kyber, false means Dilithium
    const { newPoly, N, Q, F, ROOT_OF_UNITY, brvBits} = opts;
    // Normalize JS `%` into the canonical Z_m representative `[0, modulo-1]` expected by
    // FIPS 203 §2.3 / FIPS 204 §2.3 before downstream mod-q arithmetic.
    const mod = (a, modulo = Q) => {
        const result = (a % modulo) | 0;
        return (result >= 0 ? result | 0 : (modulo + result) | 0) | 0;
    };
    // FIPS 204 §7.4 uses the centered `mod ±` representative for low bits, keeping the
    // positive midpoint when `modulo` is even.
    // Center to `[-floor((modulo-1)/2), floor(modulo/2)]`.
    const smod = (a, modulo = Q) => {
        const r = mod(a, modulo) | 0;
        return (r > modulo >> 1 ? (r - modulo) | 0 : r) | 0;
    };
    // Kyber uses the FIPS 203 Appendix A `BitRev_7` table here via the first 128 entries, while
    // Dilithium uses the FIPS 204 §7.5 / Appendix B `BitRev_8` zetas table over all 256 entries.
    function getZettas() {
        const out = newPoly(N);
        for (let i = 0; i < N; i++) {
            const b = reverseBits(i, brvBits);
            const p = BigInt(ROOT_OF_UNITY) ** BigInt(b) % BigInt(Q);
            out[i] = Number(p) | 0;
        }
        return out;
    }
    const nttZetas = getZettas();
    // Number-Theoretic Transform
    // Explained: https://electricdusk.com/ntt.html
    // Kyber has slightly different params, since there is no 512th primitive root of unity mod q,
    // only 256th primitive root of unity mod. Which also complicates MultiplyNTT.
    const inv = (_a) => {
        throw new Error('not implemented');
    };
    // ML-KEM (Kyber) polynomials always enter the transform reduced to [0, Q), so add/sub only
    // need one conditional correction instead of `%`; measured ~20% faster NTT there.
    // ML-DSA keeps the generic mod() path on purpose: its first forward stage sees centered
    // (negative) coefficients, and `sub(a, t)` can drop below -Q (t is a mul output in [0, Q)),
    // so a single correction is not enough. A guarded fast path with mod() fallback was measured
    // slower than plain `%` for the 23-bit Q (V8 int32 modulo is one div; the branches lose).
    const field = {
            add: (a, b) => mod((a | 0) + (b | 0)) | 0,
            sub: (a, b) => mod((a | 0) - (b | 0)) | 0,
            mul: (a, b) => mod((a | 0) * (b | 0)) | 0,
            inv,
        };
    const nttOpts = {
        N,
        roots: nttZetas,
        invertButterflies: true,
        skipStages: 0,
        brp: false,
    };
    const dif = FFTCore(field, { dit: false, ...nttOpts });
    const dit = FFTCore(field, { dit: true, ...nttOpts });
    const NTT = {
        encode: (r) => {
            return dif(r);
        },
        decode: (r) => {
            dit(r);
            // The inverse-NTT normalization factor is family-specific: FIPS 203 Algorithm 10 line 14
            // uses `128^-1 mod q` for Kyber, while FIPS 204 Algorithm 42 lines 21-23 use `256^-1 mod q`.
            // kyber uses 128 here, because brv && stuff
            for (let i = 0; i < r.length; i++)
                r[i] = mod(F * r[i]);
            return r;
        },
    };
    // Pack one little-endian `d`-bit word per coefficient, matching FIPS 203 ByteEncode /
    // ByteDecode and the FIPS 204 BitsToBytes-based polynomial packing helpers.
    const bitsCoder = (d, c) => {
        // Validate the carry shape once: JS bitwise operations silently truncate wider accumulators.
        for (let i = 0, bufLen = 0; i < N; i++) {
            bufLen += d;
            if (bufLen > 32)
                getMask(bufLen);
            bufLen %= 8;
        }
        const mask = getMask(d);
        const bytesLen = d * (N / 8);
        return {
            bytesLen,
            encode: (poly_) => {
                const poly = poly_;
                const r = new Uint8Array(bytesLen);
                for (let i = 0, buf = 0, bufLen = 0, pos = 0; i < poly.length; i++) {
                    buf |= (c.encode(poly[i]) & mask) << bufLen;
                    bufLen += d;
                    // Take the low byte directly: `& 0xff` matches the previous getMask(bufLen) result
                    // after Uint8Array truncation, without a validated function call per output byte.
                    for (; bufLen >= 8; bufLen -= 8, buf >>= 8)
                        r[pos++] = buf & 0xff;
                }
                return r;
            },
            decode: (bytes) => {
                const r = newPoly(N);
                for (let i = 0, buf = 0, bufLen = 0, pos = 0; i < bytes.length; i++) {
                    buf |= bytes[i] << bufLen;
                    bufLen += 8;
                    for (; bufLen >= d; bufLen -= d, buf >>= d)
                        r[pos++] = c.decode(buf & mask);
                }
                return r;
            },
        };
    };
    return {
        mod,
        smod,
        nttZetas: nttZetas,
        NTT: {
            encode: (r) => NTT.encode(r),
            decode: (r) => NTT.decode(r),
        },
        bitsCoder: bitsCoder,
    };
};
const createXofShake = (shake) => (seed, blockLen) => {
    if (!blockLen)
        blockLen = shake.blockLen;
    // Optimizations that won't mater:
    // - cached seed update (two .update(), on start and on the end)
    // - another cache which cloned into working copy
    // Faster than multiple updates, since seed less than blockLen
    const _seed = new Uint8Array(seed.length + 2);
    _seed.set(seed);
    const seedLen = seed.length;
    const buf = new Uint8Array(blockLen); // == shake128.blockLen
    let h = shake.create({});
    let calls = 0;
    let xofs = 0;
    return {
        stats: () => ({ calls, xofs }),
        get: (x, y) => {
            // Rebind to `seed || x || y` so callers can implement the spec's per-coordinate
            // SHAKE inputs like `rho || j || i` and `rho || IntegerToBytes(counter, 2)`.
            _seed[seedLen + 0] = x;
            _seed[seedLen + 1] = y;
            h.destroy();
            h = shake.create({}).update(_seed);
            calls++;
            return () => {
                xofs++;
                return h.xofInto(buf);
            };
        },
        clean: () => {
            h.destroy();
            cleanBytes(buf, _seed);
        },
    };
};
/**
 * SHAKE128-based extendable-output reader factory used by ML-KEM.
 * `get(x, y)` selects one coordinate pair at a time; calling it again invalidates previously
 * returned readers, and each squeeze reuses one mutable internal output buffer.
 * @param seed - Seed bytes for the reader.
 * @param blockLen - Optional output block length.
 * @returns Stateful XOF reader.
 * @example
 * Build the ML-KEM SHAKE128 matrix expander and read one block.
 * ```ts
 * import { randomBytes } from '@noble/post-quantum/utils.js';
 * import { XOF128 } from '@noble/post-quantum/_crystals.js';
 * const reader = XOF128(randomBytes(32));
 * const block = reader.get(0, 0)();
 * ```
 */
const XOF128 = /* @__PURE__ */ createXofShake(shake128);
/**
 * SHAKE256-based extendable-output reader factory used by ML-DSA.
 * `get(x, y)` appends raw one-byte coordinates to the seed, invalidates previously returned
 * readers, and reuses one mutable internal output buffer for each squeeze.
 * @param seed - Seed bytes for the reader.
 * @param blockLen - Optional output block length.
 * @returns Stateful XOF reader.
 * @example
 * Build the ML-DSA SHAKE256 coefficient expander and read one block.
 * ```ts
 * import { randomBytes } from '@noble/post-quantum/utils.js';
 * import { XOF256 } from '@noble/post-quantum/_crystals.js';
 * const reader = XOF256(randomBytes(32));
 * const block = reader.get(0, 0)();
 * ```
 */
const XOF256 = /* @__PURE__ */ createXofShake(shake256);

/**
 * ML-DSA: Module Lattice-based Digital Signature Algorithm from
 * [FIPS-204](https://csrc.nist.gov/pubs/fips/204/ipd). A.k.a. CRYSTALS-Dilithium.
 *
 * Has similar internals to ML-KEM, but their keys and params are different.
 * Check out [official site](https://www.pq-crystals.org/dilithium/index.shtml),
 * [repo](https://github.com/pq-crystals/dilithium).
 * @module
 */
/*! noble-post-quantum - MIT License (c) 2024 Paul Miller (paulmillr.com) */
/**
 * Keys each internal surface accepts.
 *
 * `context` is deliberately absent from both. The internal functions never read it: the
 * public wrappers consume it when they format `M'` and must not pass it down, because a
 * key that is accepted and then not acted on is the same silent downgrade this validation
 * exists to prevent. `externalMu` is the mirror case, existing here and rejected above.
 * `extraEntropy` is signing-only, so verification does not take it either.
 */
const INTERNAL_SIG_OPT_KEYS = /* @__PURE__ */ Object.freeze([
    'extraEntropy',
    'externalMu',
]);
const INTERNAL_VER_OPT_KEYS = /* @__PURE__ */ Object.freeze(['externalMu']);
function validateInternalOpts(opts, allowed) {
    const normalized = checkOptKeys(opts, allowed);
    if (normalized.externalMu !== undefined)
        abool(normalized.externalMu, 'opts.externalMu');
    return normalized;
}
// Constants
// FIPS 204 fixes ML-DSA over R = Z[X]/(X^256 + 1), so every polynomial has 256 coefficients.
const N = 256;
// 2**23 − 2**13 + 1, 23 bits: multiply will be 46. We have enough precision in JS to avoid bigints
const Q = 8380417;
// FIPS 204 §2.5 / Table 1 fixes zeta = 1753 as the 512th root of unity used by ML-DSA's NTT.
const ROOT_OF_UNITY = 1753;
// f = 256**−1 mod q, pow(256, -1, q) = 8347681 (python3)
const F = 8347681;
// FIPS 204 Table 1 / §7.4 fixes d = 13 dropped low bits for Power2Round on t.
const D = 13;
// FIPS 204 Table 1 fixes gamma2 to (q-1)/88 for ML-DSA-44 and (q-1)/32 for ML-DSA-65/87;
// §7.4 then uses alpha = 2*gamma2 for Decompose / MakeHint / UseHint.
// Dilithium is kinda parametrized over GAMMA2, but everything will break with any other value.
const GAMMA2_1 = Math.floor((Q - 1) / 88) | 0;
const GAMMA2_2 = Math.floor((Q - 1) / 32) | 0;
/** Internal params for different versions of ML-DSA  */
// prettier-ignore
/** Built-in ML-DSA parameter presets keyed by security categories `2/3/5`
 * for `ml_dsa44` / `ml_dsa65` / `ml_dsa87`.
 * This is only the Table 1 subset used directly here: `BETA = TAU * ETA` is derived later,
 * while `C_TILDE_BYTES`, `TR_BYTES`, `CRH_BYTES`, and `securityLevel` live in the preset wrappers.
 */
const PARAMS = /* @__PURE__ */ (() => Object.freeze({
    2: Object.freeze({
        K: 4, L: 4, D, GAMMA1: 2 ** 17, GAMMA2: GAMMA2_1, TAU: 39, ETA: 2, OMEGA: 80
    }),
    3: Object.freeze({
        K: 6, L: 5, D, GAMMA1: 2 ** 19, GAMMA2: GAMMA2_2, TAU: 49, ETA: 4, OMEGA: 55
    }),
    5: Object.freeze({
        K: 8, L: 7, D, GAMMA1: 2 ** 19, GAMMA2: GAMMA2_2, TAU: 60, ETA: 2, OMEGA: 75
    }),
}))();
const newPoly = (n) => new Int32Array(n);
// Shared CRYSTALS helper in the ML-DSA branch: non-Kyber mode, 8-bit bit-reversal,
// and Int32Array polys because ordinary-form coefficients can be negative / centered.
const crystals = /* @__PURE__ */ genCrystals({
    N,
    Q,
    F,
    ROOT_OF_UNITY,
    newPoly,
    brvBits: 8,
});
const id = (n) => n;
// compress()/verify() must be compatible in both directions:
// wrap the shared d-bit packer with the FIPS 204 SimpleBitPack / BitPack coefficient maps.
// malformed-input rejection only happens through the optional verify hook.
const polyCoder = (d, compress = id, verify = id) => crystals.bitsCoder(d, {
    encode: (i) => compress(verify(i)),
    decode: (i) => verify(compress(i)),
});
// Mutates `a` in place; callers must pass same-length polynomials.
// NOTE: conditional-reduction variants (as in ml-kem) were measured performance-neutral here —
// int32 `%` with 23-bit Q is already cheap — so the simpler mod() form is kept for audit.
const polyAdd = (a_, b_) => {
    const a = a_;
    const b = b_;
    for (let i = 0; i < a.length; i++)
        a[i] = crystals.mod(a[i] + b[i]);
    return a;
};
// Mutates `a` in place; callers must pass same-length polynomials.
const polySub = (a_, b_) => {
    const a = a_;
    const b = b_;
    for (let i = 0; i < a.length; i++)
        a[i] = crystals.mod(a[i] - b[i]);
    return a;
};
// Mutates `p` in place and assumes it is a decoded `t1`-range polynomial.
const polyShiftl = (p_) => {
    const p = p_;
    for (let i = 0; i < N; i++)
        p[i] <<= D;
    return p;
};
const polyChknorm = (p_, B) => {
    const p = p_;
    // FIPS 204 Algorithms 7 and 8 express the same centered-norm check with explicit inequalities.
    for (let i = 0; i < N; i++)
        if (Math.abs(crystals.smod(p[i])) >= B)
            return true;
    return false;
};
// Both inputs must already be in NTT / `T_q` form.
const MultiplyNTTs = (a_, b_) => {
    const a = a_;
    const b = b_;
    // NOTE: we don't use montgomery reduction in code, since it requires 64 bit ints,
    // which is not available in JS. mod(a[i] * b[i]) is ok, since Q is 23 bit,
    // which means a[i] * b[i] is 46 bit, which is safe to use in JS. (number is 53 bits).
    // Barrett reduction is slower than mod :(
    const c = newPoly(N);
    for (let i = 0; i < a.length; i++)
        c[i] = crystals.mod(a[i] * b[i]);
    return c;
};
// Return poly in NTT representation
function RejNTTPoly(xof_) {
    const xof = xof_;
    // Samples a polynomial ∈ Tq. xof() must return byte lengths divisible by 3.
    const r = newPoly(N);
    // NOTE: we can represent 3xu24 as 4xu32, but it doesn't improve perf :(
    for (let j = 0; j < N;) {
        const b = xof();
        if (b.length % 3)
            throw new Error('RejNTTPoly: unaligned block');
        for (let i = 0; j < N && i <= b.length - 3; i += 3) {
            // FIPS 204 Algorithm 14 clears the top bit of b2 before forming the 23-bit candidate.
            const t = (b[i + 0] | (b[i + 1] << 8) | (b[i + 2] << 16)) & 0x7fffff; // 3 bytes
            if (t < Q)
                r[j++] = t;
        }
    }
    return r;
}
// Instantiate one ML-DSA parameter set from the Table 1 lattice constants plus the
// Table 2 byte lengths / hash-width choices used by the public wrappers below.
function getDilithium(opts_) {
    const opts = opts_;
    const { K, L, GAMMA1, GAMMA2, TAU, ETA, OMEGA } = opts;
    const { CRH_BYTES, TR_BYTES, C_TILDE_BYTES, XOF128, XOF256, securityLevel } = opts;
    if (![2, 4].includes(ETA))
        throw new Error('Wrong ETA');
    if (![1 << 17, 1 << 19].includes(GAMMA1))
        throw new Error('Wrong GAMMA1');
    if (![GAMMA2_1, GAMMA2_2].includes(GAMMA2))
        throw new Error('Wrong GAMMA2');
    const BETA = TAU * ETA;
    const decompose = (r) => {
        // Decomposes r into (r1, r0) such that r ≡ r1(2γ2) + r0 mod q.
        const rPlus = crystals.mod(r);
        const r0 = crystals.smod(rPlus, 2 * GAMMA2) | 0;
        // FIPS 204 Algorithm 36 folds the top bucket `q-1` back to `(r1, r0) = (0, r0-1)`.
        if (rPlus - r0 === Q - 1)
            return { r1: 0 | 0, r0: (r0 - 1) | 0 };
        const r1 = Math.floor((rPlus - r0) / (2 * GAMMA2)) | 0;
        return { r1, r0 }; // r1 = HighBits, r0 = LowBits
    };
    const HighBits = (r) => decompose(r).r1;
    const LowBits = (r) => decompose(r).r0;
    const MakeHint = (z, r) => {
        // Compute hint bit indicating whether adding z to r alters the high bits of r.
        // FIPS 204 §6.2 also permits the Section 5.1 alternative from [6], which uses the
        // transformed low-bits/high-bits state at this call site instead of Algorithm 39 literally.
        // This optimized predicate only applies to those transformed Section 5.1 inputs; it is
        // not a drop-in replacement for Algorithm 39 on arbitrary `(z, r)` pairs.
        // From dilithium code
        const res0 = z <= GAMMA2 || z > Q - GAMMA2 || (z === Q - GAMMA2 && r === 0) ? 0 : 1;
        // from FIPS204:
        // // const r1 = HighBits(r);
        // // const v1 = HighBits(r + z);
        // // const res1 = +(r1 !== v1);
        // But they return different results! However, decompose is same.
        // So, either there is a bug in Dilithium ref implementation or in FIPS204.
        // For now, lets use dilithium one, so test vectors can be passed.
        // The round-3 Dilithium / ML-DSA code uses the same low-bits / high-bits convention after
        // `r0 += ct0`.
        // See dilithium-py README section "Optimising decomposition and making hints".
        return res0;
    };
    // m = (q-1)/(2γ2): 44 for ML-DSA-44, 16 for 65/87. Hoisted out of UseHint, which runs
    // per coefficient during verification.
    const HINT_M = Math.floor((Q - 1) / (2 * GAMMA2));
    const UseHint = (h, r) => {
        // Returns the high bits of r adjusted according to hint h
        const { r1, r0 } = decompose(r);
        // 3: if h = 1 and r0 > 0 return (r1 + 1) mod m
        // 4: if h = 1 and r0 ≤ 0 return (r1 − 1) mod m
        if (h === 1)
            return r0 > 0 ? crystals.mod(r1 + 1, HINT_M) | 0 : crystals.mod(r1 - 1, HINT_M) | 0;
        return r1 | 0;
    };
    const Power2Round = (r) => {
        // Decomposes r into (r1, r0) such that r ≡ r1*(2**d) + r0 mod q.
        const rPlus = crystals.mod(r);
        const r0 = crystals.smod(rPlus, 2 ** D) | 0;
        return { r1: Math.floor((rPlus - r0) / 2 ** D) | 0, r0 };
    };
    const hintCoder = {
        bytesLen: OMEGA + K,
        encode: (h_) => {
            const h = h_;
            if (h === false)
                throw new Error('hint.encode: hint is false'); // should never happen
            const res = new Uint8Array(OMEGA + K);
            for (let i = 0, k = 0; i < K; i++) {
                for (let j = 0; j < N; j++)
                    if (h[i][j] !== 0)
                        res[k++] = j;
                res[OMEGA + i] = k;
            }
            return res;
        },
        decode: (buf) => {
            const h = [];
            let k = 0;
            for (let i = 0; i < K; i++) {
                const hi = newPoly(N);
                if (buf[OMEGA + i] < k || buf[OMEGA + i] > OMEGA)
                    return false;
                for (let j = k; j < buf[OMEGA + i]; j++) {
                    if (j > k && buf[j] <= buf[j - 1])
                        return false;
                    hi[buf[j]] = 1;
                }
                k = buf[OMEGA + i];
                h.push(hi);
            }
            for (let j = k; j < OMEGA; j++)
                if (buf[j] !== 0)
                    return false;
            return h;
        },
    };
    const ETACoder = polyCoder(ETA === 2 ? 3 : 4, (i) => ETA - i, (i) => {
        if (!(-ETA <= i && i <= ETA))
            throw new Error(`malformed key s1/s3 ${i} outside of ETA range [${-ETA}, ${ETA}]`);
        return i;
    });
    const T0Coder = polyCoder(13, (i) => (1 << (D - 1)) - i);
    const T1Coder = polyCoder(10);
    // Requires smod. Need to fix!
    const ZCoder = polyCoder(GAMMA1 === 1 << 17 ? 18 : 20, (i) => crystals.smod(GAMMA1 - i));
    const W1Coder = polyCoder(GAMMA2 === GAMMA2_1 ? 6 : 4);
    const W1Vec = vecCoder(W1Coder, K);
    // Main structures
    const publicCoder = splitCoder('publicKey', 32, vecCoder(T1Coder, K));
    const secretCoder = splitCoder('secretKey', 32, 32, TR_BYTES, vecCoder(ETACoder, L), vecCoder(ETACoder, K), vecCoder(T0Coder, K));
    const sigCoder = splitCoder('signature', C_TILDE_BYTES, vecCoder(ZCoder, L), hintCoder);
    const CoefFromHalfByte = ETA === 2
        ? (n) => (n < 15 ? 2 - (n % 5) : false)
        : (n) => (n < 9 ? 4 - n : false);
    // Return poly in ordinary representation.
    // This helper returns ordinary-form `[-ETA, ETA]` coefficients for ExpandS; callers apply
    // `NTT.encode()` later when needed.
    function RejBoundedPoly(xof_) {
        const xof = xof_;
        // Samples an element a ∈ Rq with coeffcients in [−η, η] computed via rejection sampling from ρ.
        const r = newPoly(N);
        for (let j = 0; j < N;) {
            const b = xof();
            for (let i = 0; j < N && i < b.length; i += 1) {
                // half byte. Should be superfast with vector instructions. But very slow with js :(
                const d1 = CoefFromHalfByte(b[i] & 0x0f);
                const d2 = CoefFromHalfByte((b[i] >> 4) & 0x0f);
                if (d1 !== false)
                    r[j++] = d1;
                if (j < N && d2 !== false)
                    r[j++] = d2;
            }
        }
        return r;
    }
    const SampleInBall = (seed) => {
        // Samples a polynomial c ∈ Rq with coeffcients from {−1, 0, 1} and Hamming weight τ
        const pre = newPoly(N);
        const s = shake256.create({}).update(seed);
        const buf = new Uint8Array(shake256.blockLen);
        s.xofInto(buf);
        // FIPS 204 Algorithm 29 uses the first 8 squeezed bytes as the 64 sign bits `h`,
        // then rejection-samples coefficient positions from the remaining XOF stream.
        const masks = buf.slice(0, 8);
        for (let i = N - TAU, pos = 8, maskPos = 0, maskBit = 0; i < N; i++) {
            let b = i + 1;
            for (; b > i;) {
                b = buf[pos++];
                if (pos < shake256.blockLen)
                    continue;
                s.xofInto(buf);
                pos = 0;
            }
            pre[i] = pre[b];
            pre[b] = 1 - (((masks[maskPos] >> maskBit++) & 1) << 1);
            if (maskBit >= 8) {
                maskPos++;
                maskBit = 0;
            }
        }
        return pre;
    };
    const polyPowerRound = (p_) => {
        const p = p_;
        const res0 = newPoly(N);
        const res1 = newPoly(N);
        for (let i = 0; i < p.length; i++) {
            const { r0, r1 } = Power2Round(p[i]);
            res0[i] = r0;
            res1[i] = r1;
        }
        return { r0: res0, r1: res1 };
    };
    const polyUseHint = (u_, h_) => {
        const u = u_;
        const h = h_;
        // In-place on `u`: verification only needs the recovered high bits, so reuse the
        // temporary `wApprox` buffer instead of allocating another polynomial.
        for (let i = 0; i < N; i++)
            u[i] = UseHint(h[i], u[i]);
        return u;
    };
    const polyMakeHint = (a_, b_) => {
        const a = a_;
        const b = b_;
        const v = newPoly(N);
        let cnt = 0;
        for (let i = 0; i < N; i++) {
            const h = MakeHint(a[i], b[i]);
            v[i] = h;
            cnt += h;
        }
        return { v, cnt };
    };
    const signRandBytes = 32;
    const seedCoder = splitCoder('seed', 32, 64, 32);
    // API & argument positions are exactly as in FIPS204.
    const internal = Object.freeze({
        info: Object.freeze({ type: 'internal-ml-dsa' }),
        lengths: Object.freeze({
            secretKey: secretCoder.bytesLen,
            publicKey: publicCoder.bytesLen,
            seed: 32,
            signature: sigCoder.bytesLen,
            signRand: signRandBytes,
        }),
        keygen: (seed) => {
            // H(𝜉||IntegerToBytes(𝑘, 1)||IntegerToBytes(ℓ, 1), 128) 2: ▷ expand seed
            const seedDst = new Uint8Array(32 + 2);
            const randSeed = seed === undefined;
            if (randSeed)
                seed = randomBytes(32);
            abytesDoc(seed, 32, 'seed');
            seedDst.set(seed);
            if (randSeed)
                cleanBytes(seed);
            seedDst[32] = K;
            seedDst[33] = L;
            const [rho, rhoPrime, K_] = seedCoder.decode(shake256(seedDst, { dkLen: seedCoder.bytesLen }));
            const xofPrime = XOF256(rhoPrime);
            const s1 = [];
            for (let i = 0; i < L; i++)
                s1.push(RejBoundedPoly(xofPrime.get(i & 0xff, (i >> 8) & 0xff)));
            const s2 = [];
            for (let i = L; i < L + K; i++)
                s2.push(RejBoundedPoly(xofPrime.get(i & 0xff, (i >> 8) & 0xff)));
            const s1Hat = s1.map((i) => crystals.NTT.encode(i.slice()));
            const t0 = [];
            const t1 = [];
            const xof = XOF128(rho);
            const t = newPoly(N);
            for (let i = 0; i < K; i++) {
                // t ← NTT−1(A*NTT(s1)) + s2
                cleanBytes(t); // don't-reallocate
                for (let j = 0; j < L; j++) {
                    const aij = RejNTTPoly(xof.get(j, i)); // super slow!
                    polyAdd(t, MultiplyNTTs(aij, s1Hat[j]));
                }
                crystals.NTT.decode(t);
                const { r0, r1 } = polyPowerRound(polyAdd(t, s2[i])); // (t1, t0) ← Power2Round(t, d)
                t0.push(r0);
                t1.push(r1);
            }
            const publicKey = publicCoder.encode([rho, t1]); // pk ← pkEncode(ρ, t1)
            const tr = shake256(publicKey, { dkLen: TR_BYTES }); // tr ← H(BytesToBits(pk), 512)
            // sk ← skEncode(ρ, K,tr, s1, s2, t0)
            const secretKey = secretCoder.encode([rho, K_, tr, s1, s2, t0]);
            xof.clean();
            xofPrime.clean();
            // STATS
            // Kyber512: { calls: 4, xofs: 12 }, Kyber768: { calls: 9, xofs: 27 },
            // Kyber1024: { calls: 16, xofs: 48 }
            // DSA44: { calls: 24, xofs: 24 }, DSA65: { calls: 41, xofs: 41 },
            // DSA87: { calls: 71, xofs: 71 }
            cleanBytes(rho, rhoPrime, K_, s1, s2, s1Hat, t, t0, t1, tr, seedDst);
            return {
                publicKey: publicKey,
                secretKey: secretKey,
            };
        },
        getPublicKey: (secretKey) => {
            // (ρ, K,tr, s1, s2, t0) ← skDecode(sk)
            const [rho, _K, _tr, s1, s2, _t0] = secretCoder.decode(secretKey);
            const xof = XOF128(rho);
            const s1Hat = s1.map((p) => crystals.NTT.encode(p.slice()));
            const t1 = [];
            const tmp = newPoly(N);
            for (let i = 0; i < K; i++) {
                tmp.fill(0);
                for (let j = 0; j < L; j++) {
                    const aij = RejNTTPoly(xof.get(j, i)); // A_ij in NTT
                    polyAdd(tmp, MultiplyNTTs(aij, s1Hat[j])); // += A_ij * s1_j
                }
                crystals.NTT.decode(tmp); // NTT⁻¹
                polyAdd(tmp, s2[i]); // t_i = A·s1 + s2
                const { r1 } = polyPowerRound(tmp); // r1 = t1, r0 ≈ t0
                t1.push(r1);
            }
            xof.clean();
            cleanBytes(tmp, s1Hat, _t0, s1, s2);
            return publicCoder.encode([rho, t1]);
        },
        // NOTE: random is optional.
        sign: (msg, secretKey, opts = {}) => {
            opts = validateSigOpts(opts, INTERNAL_SIG_OPT_KEYS);
            opts = validateInternalOpts(opts, INTERNAL_SIG_OPT_KEYS);
            const { extraEntropy: random, externalMu = false } = opts;
            // FIPS 204 external-mu mode expects the 64-byte message representative µ = H(tr || M).
            if (externalMu)
                abytesDoc(msg, CRH_BYTES, 'mu');
            // Prepare entropy before touching decoded secrets: randomBytes() may throw, and an RNG
            // failure must not leave expanded secret-polynomial copies behind.
            const ownRnd = random === false || random === undefined;
            const rnd = random === false
                ? new Uint8Array(32)
                : random === undefined
                    ? randomBytes(signRandBytes)
                    : random;
            abytesDoc(rnd, 32, 'extraEntropy');
            // This part can be pre-cached per secretKey, but there is only minor performance improvement,
            // since we re-use a lot of variables to computation.
            // (ρ, K,tr, s1, s2, t0) ← skDecode(sk)
            const decoded = (() => {
                try {
                    return secretCoder.decode(secretKey);
                }
                catch (error) {
                    // A malformed key must not strand entropy owned by the library.
                    if (ownRnd)
                        cleanBytes(rnd);
                    throw error;
                }
            })();
            const [rho, _K, tr, s1, s2, t0] = decoded;
            // Cache matrix to avoid re-compute later
            const A = []; // A ← ExpandA(ρ)
            const xof = XOF128(rho);
            for (let i = 0; i < K; i++) {
                const pv = [];
                for (let j = 0; j < L; j++)
                    pv.push(RejNTTPoly(xof.get(j, i)));
                A.push(pv);
            }
            xof.clean();
            for (let i = 0; i < L; i++)
                crystals.NTT.encode(s1[i]); // sˆ1 ← NTT(s1)
            for (let i = 0; i < K; i++) {
                crystals.NTT.encode(s2[i]); // sˆ2 ← NTT(s2)
                crystals.NTT.encode(t0[i]); // tˆ0 ← NTT(t0)
            }
            // This part is per msg
            const mu = externalMu
                ? msg
                : // 6: µ ← H(tr||M, 512)
                    //    ▷ Compute message representative µ
                    shake256.create({ dkLen: CRH_BYTES }).update(tr).update(msg).digest();
            const rhoprime = shake256
                .create({ dkLen: CRH_BYTES })
                .update(_K)
                .update(rnd)
                .update(mu)
                .digest(); // ρ′← H(K||rnd||µ, 512)
            // Only wipe entropy we generated; caller-provided extraEntropy stays caller-owned.
            if (ownRnd)
                cleanBytes(rnd);
            abytesDoc(rhoprime, CRH_BYTES);
            const x256 = XOF256(rhoprime, ZCoder.bytesLen);
            //  Rejection sampling loop
            main_loop: for (let kappa = 0;;) {
                const y = [];
                // y ← ExpandMask(ρ , κ)
                for (let i = 0; i < L; i++, kappa++)
                    y.push(ZCoder.decode(x256.get(kappa & 0xff, kappa >> 8)()));
                const z = y.map((i) => crystals.NTT.encode(i.slice()));
                const w = [];
                for (let i = 0; i < K; i++) {
                    // w ← NTT−1(A ◦ NTT(y))
                    const wi = newPoly(N);
                    for (let j = 0; j < L; j++)
                        polyAdd(wi, MultiplyNTTs(A[i][j], z[j]));
                    crystals.NTT.decode(wi);
                    w.push(wi);
                }
                const w1 = w.map((j) => j.map(HighBits)); // w1 ← HighBits(w)
                // Commitment hash: c˜ ∈{0, 1 2λ } ← H(µ||w1Encode(w1), 2λ)
                const cTilde = shake256
                    .create({ dkLen: C_TILDE_BYTES })
                    .update(mu)
                    .update(W1Vec.encode(w1))
                    .digest();
                // Verifer’s challenge
                // c ← SampleInBall(c˜1); cˆ ← NTT(c)
                const cHat = crystals.NTT.encode(SampleInBall(cTilde));
                // ⟨⟨cs1⟩⟩ ← NTT−1(cˆ◦ sˆ1)
                const cs1 = s1.map((i) => MultiplyNTTs(i, cHat));
                for (let i = 0; i < L; i++) {
                    polyAdd(crystals.NTT.decode(cs1[i]), y[i]); // z ← y + ⟨⟨cs1⟩⟩
                    if (polyChknorm(cs1[i], GAMMA1 - BETA)) {
                        // Rejected. Wipe this iteration's secret-derived buffers before retrying; the
                        // accepted path wipes the same set, and only the persistent key material (s1, s2,
                        // t0, A, rhoprime) is kept for the next iteration and cleaned at the very end.
                        cleanBytes(cTilde, cs1, cHat, w1, w, z, y);
                        continue main_loop; // ||z||∞ ≥ γ1 − β
                    }
                }
                // cs1 is now z (▷ Signer’s response)
                let cnt = 0;
                const h = [];
                for (let i = 0; i < K; i++) {
                    const cs2 = crystals.NTT.decode(MultiplyNTTs(s2[i], cHat)); // ⟨⟨cs2⟩⟩ ← NTT−1(cˆ◦ sˆ2)
                    const r0 = polySub(w[i], cs2).map(LowBits); // r0 ← LowBits(w − ⟨⟨cs2⟩⟩)
                    if (polyChknorm(r0, GAMMA2 - BETA)) {
                        cleanBytes(cTilde, cs1, cHat, w1, w, z, y, h, cs2, r0);
                        continue main_loop; // ||r0||∞ ≥ γ2 − β
                    }
                    const ct0 = crystals.NTT.decode(MultiplyNTTs(t0[i], cHat)); // ⟨⟨ct0⟩⟩ ← NTT−1(cˆ◦ tˆ0)
                    if (polyChknorm(ct0, GAMMA2)) {
                        cleanBytes(cTilde, cs1, cHat, w1, w, z, y, h, cs2, r0, ct0);
                        continue main_loop;
                    }
                    polyAdd(r0, ct0);
                    // ▷ Signer’s hint
                    const hint = polyMakeHint(r0, w1[i]); // h ← MakeHint(−⟨⟨ct0⟩⟩, w− ⟨⟨cs2⟩⟩ + ⟨⟨ct0⟩⟩)
                    h.push(hint.v);
                    cnt += hint.cnt;
                }
                if (cnt > OMEGA) {
                    cleanBytes(cTilde, cs1, cHat, w1, w, z, y, h);
                    continue; // the number of 1’s in h is greater than ω
                }
                x256.clean();
                const res = sigCoder.encode([cTilde, cs1, h]); // σ ← sigEncode(c˜, z mod±q, h)
                // rho, _K, tr is subarray of secretKey, cannot clean.
                cleanBytes(cTilde, cs1, h, cHat, w1, w, z, y, rhoprime, s1, s2, t0, ...A);
                // `externalMu` hands ownership of `mu` to the caller,
                // so only wipe the internally derived digest form here;
                // zeroizing caller memory would break the caller's own reuse / verify path.
                if (!externalMu)
                    cleanBytes(mu);
                return res;
            }
            // @ts-ignore
            throw new Error('Unreachable code path reached, report this error');
        },
        verify: (sig, msg, publicKey, opts = {}) => {
            opts = validateInternalOpts(opts, INTERNAL_VER_OPT_KEYS);
            const { externalMu = false } = opts;
            // FIPS 204 external-mu mode expects the 64-byte message representative µ = H(tr || M).
            if (externalMu)
                abytesDoc(msg, CRH_BYTES, 'mu');
            // ML-DSA.Verify(pk, M, σ): Verifes a signature σ for a message M.
            const [rho, t1] = publicCoder.decode(publicKey); // (ρ, t1) ← pkDecode(pk)
            const tr = shake256(publicKey, { dkLen: TR_BYTES }); // 6: tr ← H(BytesToBits(pk), 512)
            if (sig.length !== sigCoder.bytesLen)
                return false; // return false instead of exception
            // (c˜, z, h) ← sigDecode(σ)
            // ▷ Signer’s commitment hash c ˜, response z and hint
            const [cTilde, z, h] = sigCoder.decode(sig);
            if (h === false)
                return false; // if h = ⊥ then return false
            for (let i = 0; i < L; i++)
                if (polyChknorm(z[i], GAMMA1 - BETA))
                    return false;
            const mu = externalMu
                ? msg
                : // 7: µ ← H(tr||M, 512)
                    shake256.create({ dkLen: CRH_BYTES }).update(tr).update(msg).digest();
            // Compute verifer’s challenge from c˜
            const c = crystals.NTT.encode(SampleInBall(cTilde)); // c ← SampleInBall(c˜1)
            const zNtt = z.map((i) => i.slice()); // zNtt = NTT(z)
            for (let i = 0; i < L; i++)
                crystals.NTT.encode(zNtt[i]);
            const wTick1 = [];
            const xof = XOF128(rho);
            for (let i = 0; i < K; i++) {
                const ct12d = MultiplyNTTs(crystals.NTT.encode(polyShiftl(t1[i])), c); //c * t1 * (2**d)
                const Az = newPoly(N); // // A * z
                for (let j = 0; j < L; j++) {
                    const aij = RejNTTPoly(xof.get(j, i)); // A[i][j] inplace
                    polyAdd(Az, MultiplyNTTs(aij, zNtt[j]));
                }
                // wApprox = A*z - c*t1 * (2**d)
                const wApprox = crystals.NTT.decode(polySub(Az, ct12d));
                // Reconstruction of signer’s commitment
                wTick1.push(polyUseHint(wApprox, h[i])); // w ′ ← UseHint(h, w'approx )
            }
            xof.clean();
            // c˜′← H (µ||w1Encode(w′1), 2λ),  Hash it; this should match c˜
            const c2 = shake256
                .create({ dkLen: C_TILDE_BYTES })
                .update(mu)
                .update(W1Vec.encode(wTick1))
                .digest();
            // Additional checks in FIPS-204:
            // [[ ||z||∞ < γ1 − β ]] and [[c ˜ = c˜′]] and [[number of 1’s in h is ≤ ω]]
            for (const t of h) {
                const sum = t.reduce((acc, i) => acc + i, 0);
                if (!(sum <= OMEGA))
                    return false;
            }
            for (const t of z)
                if (polyChknorm(t, GAMMA1 - BETA))
                    return false;
            return equalBytes(cTilde, c2);
        },
    });
    return Object.freeze({
        info: Object.freeze({ type: 'ml-dsa' }),
        internal,
        securityLevel: securityLevel,
        keygen: internal.keygen,
        lengths: internal.lengths,
        getPublicKey: internal.getPublicKey,
        sign: (msg, secretKey, opts = {}) => {
            opts = validateSigOpts(opts);
            const M = getMessage(msg, opts.context);
            // `context` is consumed by getMessage() above; forwarding it would make the internal
            // surface accept a key it never reads.
            const res = internal.sign(M, secretKey, {
                extraEntropy: opts.extraEntropy,
                externalMu: false,
            });
            cleanBytes(M);
            return res;
        },
        verify: (sig, msg, publicKey, opts = {}) => {
            opts = validateVerOpts(opts);
            abytesDoc(sig, undefined, 'signature');
            return internal.verify(sig, getMessage(msg, opts.context), publicKey, { externalMu: false });
        },
        prehash: (hash) => {
            checkHash(hash, securityLevel);
            const rawHash = hash;
            return Object.freeze({
                info: Object.freeze({ type: 'hashml-dsa' }),
                securityLevel: securityLevel,
                lengths: internal.lengths,
                keygen: internal.keygen,
                getPublicKey: internal.getPublicKey,
                sign: (msg, secretKey, opts = {}) => {
                    opts = validateSigOpts(opts);
                    const M = getMessagePrehash(rawHash, msg, opts.context);
                    // As above: getMessagePrehash() consumes `context`, so it must not travel further.
                    const res = internal.sign(M, secretKey, {
                        extraEntropy: opts.extraEntropy,
                        externalMu: false,
                    });
                    cleanBytes(M);
                    return res;
                },
                verify: (sig, msg, publicKey, opts = {}) => {
                    opts = validateVerOpts(opts);
                    abytesDoc(sig, undefined, 'signature');
                    return internal.verify(sig, getMessagePrehash(rawHash, msg, opts.context), publicKey, {
                        externalMu: false,
                    });
                },
            });
        },
    });
}
/** ML-DSA-65 for 192-bit security level. Not recommended after 2030, as per ASD. */
const ml_dsa65 = /* @__PURE__ */ (() => getDilithium({
    ...PARAMS[3],
    CRH_BYTES: 64,
    TR_BYTES: 64,
    C_TILDE_BYTES: 48,
    XOF128,
    XOF256,
    securityLevel: 192,
}))();

hashes.sha512 = sha512;

/**
 * Signs a message using an Ed25519 seed or private key.
 * @param {Uint8Array} seed 32-byte Ed25519 seed.
 * @param {Uint8Array} message Data to sign.
 * @returns {Promise<Uint8Array>} 64-byte signature.
 */
async function signEd25519(seed, message) {
  return sign(message, seed);
}

/**
 * Signs a message using ML-DSA-65 key material.
 * @param {{ seed: Uint8Array|null, secretKey: Uint8Array|null }} keyMaterial
 * @param {Uint8Array} message
 * @returns {Promise<Uint8Array>} 3309-byte signature.
 */
async function signMlDsa65(keyMaterial, message) {
  let sk = keyMaterial.secretKey;
  if (!sk && keyMaterial.seed) {
    const pair = ml_dsa65.keygen(keyMaterial.seed);
    sk = pair.secretKey;
  }
  if (!sk) {
    throw new Error('Invalid ML-DSA-65 key material.');
  }
  return ml_dsa65.sign(message, sk);
}

/**
 * Decrypts an AES-256-GCM ciphertext using PBKDF2-SHA256 derived key.
 * @param {string} passphrase User passphrase.
 * @param {Uint8Array} salt 16-byte PBKDF2 salt and AES-GCM additionalData.
 * @param {Uint8Array} nonce 12-byte AES-GCM nonce.
 * @param {Uint8Array} ciphertextWithTag Ciphertext followed by 16-byte GCM tag.
 * @returns {Promise<Uint8Array>} Decrypted plaintext bytes.
 */
async function decryptAesGcmPbkdf2(passphrase, salt, nonce, ciphertextWithTag) {
  const enc = new TextEncoder();
  const baseKey = await globalThis.crypto.subtle.importKey(
    'raw',
    enc.encode(passphrase),
    { name: 'PBKDF2' },
    false,
    ['deriveKey']
  );

  const aesKey = await globalThis.crypto.subtle.deriveKey(
    {
      name: 'PBKDF2',
      salt: salt,
      iterations: 100000,
      hash: 'SHA-256'
    },
    baseKey,
    { name: 'AES-GCM', length: 256 },
    false,
    ['decrypt']
  );

  const decrypted = await globalThis.crypto.subtle.decrypt(
    {
      name: 'AES-GCM',
      iv: nonce,
      additionalData: salt,
      tagLength: 128
    },
    aesKey,
    ciphertextWithTag
  );

  return new Uint8Array(decrypted);
}

/**
 * Encrypts a plaintext buffer using AES-256-GCM with a PBKDF2-SHA256 derived key.
 * @param {string} passphrase User passphrase.
 * @param {Uint8Array} salt 16-byte PBKDF2 salt and AES-GCM additionalData.
 * @param {Uint8Array} nonce 12-byte AES-GCM nonce.
 * @param {Uint8Array} plaintext Plaintext bytes to encrypt.
 * @returns {Promise<Uint8Array>} Encrypted bytes including 16-byte GCM authentication tag.
 */
async function encryptAesGcmPbkdf2(passphrase, salt, nonce, plaintext) {
  const enc = new TextEncoder();
  const baseKey = await globalThis.crypto.subtle.importKey(
    'raw',
    enc.encode(passphrase),
    { name: 'PBKDF2' },
    false,
    ['deriveKey']
  );

  const aesKey = await globalThis.crypto.subtle.deriveKey(
    {
      name: 'PBKDF2',
      salt: salt,
      iterations: 100000,
      hash: 'SHA-256'
    },
    baseKey,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt']
  );

  const encrypted = await globalThis.crypto.subtle.encrypt(
    {
      name: 'AES-GCM',
      iv: nonce,
      additionalData: salt,
      tagLength: 128
    },
    aesKey,
    plaintext
  );

  return new Uint8Array(encrypted);
}

/**
 * Generates an Ed25519 keypair and DER-encoded SPKI/PKCS#8 structures.
 * @param {Uint8Array} [seed] Optional 32-byte seed. If omitted, cryptographically random bytes are generated.
 * @returns {Promise<{ seed: Uint8Array, publicKey: Uint8Array, spkiDer: Uint8Array, pkcs8Der: Uint8Array }>}
 */
async function keygenEd25519(seed) {
  let s;
  if (seed instanceof Uint8Array && seed.length === 32) {
    s = new Uint8Array(seed);
  } else {
    s = new Uint8Array(32);
    globalThis.crypto.getRandomValues(s);
  }
  const ext = getExtendedPublicKey(s);
  const rawPub = ext.pointBytes;

  const spkiDer = new Uint8Array(44);
  spkiDer.set([0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00], 0);
  spkiDer.set(rawPub, 12);

  const pkcs8Der = new Uint8Array(48);
  pkcs8Der.set([0x30, 0x2e, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x04, 0x22, 0x04, 0x20], 0);
  pkcs8Der.set(s, 16);

  return { seed: s, publicKey: rawPub, spkiDer, pkcs8Der };
}

/**
 * Generates an ML-DSA-65 keypair and DER-encoded SPKI/PKCS#8 structures.
 * @param {Uint8Array} [seed] Optional 32-byte seed. If omitted, cryptographically random bytes are generated.
 * @returns {Promise<{ seed: Uint8Array, publicKey: Uint8Array, secretKey: Uint8Array, spkiDer: Uint8Array, pkcs8Der: Uint8Array }>}
 */
async function keygenMlDsa65(seed) {
  let s;
  if (seed instanceof Uint8Array && seed.length === 32) {
    s = new Uint8Array(seed);
  } else {
    s = new Uint8Array(32);
    globalThis.crypto.getRandomValues(s);
  }
  const pair = ml_dsa65.keygen(s);

  const spkiDer = new Uint8Array(1974);
  spkiDer.set([
    0x30, 0x82, 0x07, 0xb2,
    0x30, 0x0b, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x03, 0x12,
    0x03, 0x82, 0x07, 0xa1, 0x00
  ], 0);
  spkiDer.set(pair.publicKey, 22);

  const pkcs8Der = new Uint8Array(4098);
  pkcs8Der.set([
    0x30, 0x82, 0x10, 0x02,
    0x02, 0x01, 0x00,
    0x30, 0x0b, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x03, 0x12,
    0x04, 0x82, 0x0f, 0xec,
    0x30, 0x82, 0x0f, 0xe6,
    0x04, 0x20
  ], 0);
  pkcs8Der.set(s, 30);
  pkcs8Der.set([0x04, 0x82, 0x0f, 0xc0], 62);
  pkcs8Der.set(pair.secretKey, 66);

  return {
    seed: s,
    publicKey: pair.publicKey,
    secretKey: pair.secretKey,
    spkiDer,
    pkcs8Der
  };
}

const WeaverCryptoVendor = {
  signEd25519,
  signMlDsa65,
  decryptAesGcmPbkdf2,
  encryptAesGcmPbkdf2,
  keygenEd25519,
  keygenMlDsa65
};

if (typeof window !== 'undefined') {
  window.WeaverCryptoVendor = WeaverCryptoVendor;
}
if (typeof globalThis !== 'undefined') {
  globalThis.WeaverCryptoVendor = WeaverCryptoVendor;
}

export {
  decryptAesGcmPbkdf2,
  encryptAesGcmPbkdf2,
  keygenEd25519,
  keygenMlDsa65,
  WeaverCryptoVendor as default,
  signEd25519,
  signMlDsa65
};
