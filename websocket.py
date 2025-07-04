import asyncio, json, numpy as np, sounddevice as sd, websockets

FS = 44100
WINDOW = 44100                # 1 s
OVERLAP = WINDOW // 2
BETA_MIN_F, BETA_MAX_F = 12, 30

clients = set()
buffer = np.empty(0, dtype=np.float32)

async def broadcast(msg: str):
    dead = set()
    for c in clients:
        try:
            await c.send(msg)
        except websockets.exceptions.ConnectionClosed:
            dead.add(c)
    clients.difference_update(dead)

async def handler(ws):
    clients.add(ws)
    try:
        await asyncio.Future()          # keep open
    finally:
        clients.discard(ws)

def process_block(block, min_b, max_b):
    win = np.hamming(len(block))
    X = np.fft.rfft(block * win)
    P = np.abs(X) ** 2
    k = np.arange(len(P)) * FS / (2 * (len(P)-1))
    beta_band = (k >= BETA_MIN_F) & (k <= BETA_MAX_F)
    beta_power = P[beta_band].sum()
    min_b = beta_power if min_b is None else min(min_b, beta_power)
    max_b = beta_power if max_b is None else max(max_b, beta_power)
    if max_b == min_b:
        return None, min_b, max_b
    value = int(round((beta_power - min_b) / (max_b - min_b) * 100))
    return value, min_b, max_b

async def audio_loop():
    global buffer
    min_beta = max_beta = None
    with sd.InputStream(samplerate=FS, channels=1, dtype='float32') as stream:
        while True:
            data, _ = stream.read(OVERLAP)
            buffer = np.concatenate((buffer, data[:, 0]))
            while len(buffer) >= WINDOW:
                block, buffer = buffer[:WINDOW], buffer[OVERLAP:]
                val, min_beta, max_beta = process_block(block, min_beta, max_beta)
                if val is not None:
                    await broadcast(json.dumps({"value": val}))
            await asyncio.sleep(0)

async def main():
    audio_task = asyncio.create_task(audio_loop())
    async with websockets.serve(handler, "0.0.0.0", 4649):
        await audio_task

if __name__ == "__main__":
    asyncio.run(main())
