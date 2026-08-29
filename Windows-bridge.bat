@echo off
setlocal

where node >nul 2>nul
if errorlevel 1 (
	echo Node.js 20 or newer is required.
	echo Install it from https://nodejs.org and run this file again.
	pause
	exit /b 1
)

node -e "process.exit(Math.sign(Number(process.versions.node.split('.')[0]) - 20) === -1 ? 1 : 0)" >nul 2>nul
if errorlevel 1 (
	echo Node.js 20 or newer is required.
	echo Update Node.js from https://nodejs.org and run this file again.
	pause
	exit /b 1
)

for /f "tokens=1 delims=:" %%L in ('findstr /n /x "::ROIMPORT_BRIDGE" "%~f0"') do set "BRIDGE_LINE=%%L"
if not defined BRIDGE_LINE (
	echo Embedded bridge source could not be found.
	pause
	exit /b 1
)

set "BRIDGE_FILE=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$lines = Get-Content -LiteralPath $env:BRIDGE_FILE; $start = [int]$env:BRIDGE_LINE; $source = $lines[$start..($lines.Length - 1)] -join [Environment]::NewLine; $source | & node -"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" echo The server has stopped with exit code %EXIT_CODE%.
pause
exit /b %EXIT_CODE%

::ROIMPORT_BRIDGE
const http = require("node:http");
const zlib = require("node:zlib");

const HOST = "127.0.0.1";
const PORT = 27123;
const BRIDGE_VERSION = 3;
const MAX_BODY_SIZE = 32 * 1024 * 1024;
const ROBLOX_ASSET_URL = "https://apis.roblox.com/assets/v1/assets";
const ROBLOX_OPERATION_URL = "https://apis.roblox.com/assets/v1/operations";
const PNG_SIGNATURE = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

function getCorsHeaders() {
	return {
		"Access-Control-Allow-Origin": "*",
		"Access-Control-Allow-Headers": "Content-Type",
		"Access-Control-Allow-Methods": "GET, POST, OPTIONS",
		"Cache-Control": "no-store",
	};
}

function sendJson(response, statusCode, body) {
	response.writeHead(statusCode, {
		...getCorsHeaders(),
		"Content-Type": "application/json; charset=utf-8",
	});
	response.end(JSON.stringify(body));
}

function readJsonBody(request) {
	return new Promise((resolve, reject) => {
		const chunks = [];
		let size = 0;

		request.on("data", (chunk) => {
			size += chunk.length;

			if (size > MAX_BODY_SIZE) {
				reject(new Error("Image payload is too large."));
				request.destroy();
				return;
			}

			chunks.push(chunk);
		});

		request.on("end", () => {
			try {
				resolve(JSON.parse(Buffer.concat(chunks).toString("utf8")));
			} catch {
				reject(new Error("The bridge has received invalid JSON."));
			}
		});

		request.on("error", reject);
	});
}

function validateUpload(payload) {
	if (!payload.apiKey || typeof payload.apiKey !== "string") {
		throw new Error("The APIKey is required.");
	}

	if (payload.creatorType !== "user" && payload.creatorType !== "group") {
		throw new Error("Creator type must be user or group.");
	}

	if (!/^\d+$/.test(String(payload.creatorId || ""))) {
		throw new Error("A valid user/group ID is required.");
	}

	if (!payload.fileName || !payload.data) {
		throw new Error("Image file data is missing.");
	}
}

function getDisplayName(fileName) {
	const name = String(fileName).replace(/\.[^.]+$/, "").trim();
	return (name || "RoImport Image").slice(0, 50);
}

function createAssetMetadata(payload) {
	const creatorKey = payload.creatorType === "group" ? "groupId" : "userId";

	return {
		assetType: "Image",
		displayName: getDisplayName(payload.fileName),
		description: "Uploaded by the RoImport local bridge.",
		creationContext: {
			creator: {
				[creatorKey]: String(payload.creatorId),
			},
		},
	};
}

async function parseRobloxResponse(response) {
	const text = await response.text();
	let data = {};

	if (text) {
		try {
			data = JSON.parse(text);
		} catch {
			data = { message: text };
		}
	}

	if (!response.ok) {
		throw new Error(data.message || data.error || `Roblox returned status ${response.status}.`);
	}

	return data;
}

function getOperationId(data) {
	const path = data.path || data.operationPath || data.operation?.path;
	const directId = data.operationId || data.operation?.operationId;

	if (directId) {
		return String(directId);
	}

	return typeof path === "string" ? path.split("/").pop() : "";
}

function getAssetId(data) {
	const candidates = [
		data.assetId,
		data.asset?.assetId,
		data.response?.assetId,
		data.response?.asset?.assetId,
		data.response?.path,
		data.asset?.path,
	];

	for (const candidate of candidates) {
		const match = String(candidate || "").match(/(?:assets\/)?(\d+)$/);

		if (match) {
			return match[1];
		}
	}

	return "";
}

function sleep(milliseconds) {
	return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function getCompletedAssetId(operation) {
	if (!operation.done) {
		return "";
	}

	if (operation.error) {
		throw new Error(operation.error.message || "Roblox rejected the image upload.");
	}

	const assetId = getAssetId(operation);

	if (!assetId) {
		throw new Error("Roblox completed the upload without an image asset ID.");
	}

	return assetId;
}

async function pollOperation(operationId, apiKey) {
	for (let attempt = 0; attempt < 120; attempt++) {
		const response = await fetch(`${ROBLOX_OPERATION_URL}/${operationId}`, {
			headers: { "x-api-key": apiKey },
		});
		const operation = await parseRobloxResponse(response);
		const assetId = getCompletedAssetId(operation);

		if (assetId) {
			return assetId;
		}

		await sleep(1000);
	}

	throw new Error("Roblox did not finish processing the image in time.");
}

function readPngChunks(bytes) {
	if (bytes.length < 8 || !bytes.subarray(0, 8).equals(PNG_SIGNATURE)) {
		throw new Error("Pixfix only supports PNG images.");
	}

	const chunks = [];
	let offset = 8;

	while (offset + 12 <= bytes.length) {
		const length = bytes.readUInt32BE(offset);
		const type = bytes.toString("ascii", offset + 4, offset + 8);
		const dataStart = offset + 8;
		const dataEnd = dataStart + length;

		if (dataEnd + 4 > bytes.length) {
			throw new Error("PNG data is incomplete.");
		}

		chunks.push({
			type,
			data: bytes.subarray(dataStart, dataEnd),
		});
		offset = dataEnd + 4;

		if (type === "IEND") {
			break;
		}
	}

	return chunks;
}

function getPngBytesPerPixel(colorType) {
	if (colorType === 6) return 4;
	if (colorType === 2) return 3;
	if (colorType === 4) return 2;
	if (colorType === 0) return 1;
	throw new Error("Pixfix does not support this PNG color type.");
}

function paethPredictor(a, b, c) {
	const p = a + b - c;
	const pa = Math.abs(p - a);
	const pb = Math.abs(p - b);
	const pc = Math.abs(p - c);

	if (pa <= pb && pa <= pc) return a;
	if (pb <= pc) return b;
	return c;
}

function unfilterPngScanlines(data, width, height, bytesPerPixel) {
	const rowLength = width * bytesPerPixel;
	const output = Buffer.alloc(rowLength * height);
	let sourceOffset = 0;

	for (let y = 0; y < height; y++) {
		const filter = data[sourceOffset++];
		const rowOffset = y * rowLength;
		const previousOffset = rowOffset - rowLength;

		for (let x = 0; x < rowLength; x++) {
			const raw = data[sourceOffset++];
			const left = x >= bytesPerPixel ? output[rowOffset + x - bytesPerPixel] : 0;
			const up = y > 0 ? output[previousOffset + x] : 0;
			const upLeft =
				y > 0 && x >= bytesPerPixel
					? output[previousOffset + x - bytesPerPixel]
					: 0;
			let value;

			if (filter === 0) value = raw;
			else if (filter === 1) value = raw + left;
			else if (filter === 2) value = raw + up;
			else if (filter === 3) value = raw + Math.floor((left + up) / 2);
			else if (filter === 4) value = raw + paethPredictor(left, up, upLeft);
			else throw new Error("Unsupported PNG filter.");

			output[rowOffset + x] = value & 255;
		}
	}

	return output;
}

function decodePng(bytes) {
	const chunks = readPngChunks(bytes);
	const header = chunks.find((chunk) => chunk.type === "IHDR");

	if (!header || header.data.length !== 13) {
		throw new Error("PNG header is missing.");
	}

	const width = header.data.readUInt32BE(0);
	const height = header.data.readUInt32BE(4);
	const bitDepth = header.data[8];
	const colorType = header.data[9];
	const interlace = header.data[12];

	if (bitDepth !== 8) {
		throw new Error("Pixfix currently supports 8-bit PNG images.");
	}

	if (interlace !== 0) {
		throw new Error("Pixfix currently supports non-interlaced PNG images.");
	}

	const bytesPerPixel = getPngBytesPerPixel(colorType);
	const compressed = Buffer.concat(
		chunks.filter((chunk) => chunk.type === "IDAT").map((chunk) => chunk.data),
	);
	const raw = unfilterPngScanlines(
		zlib.inflateSync(compressed),
		width,
		height,
		bytesPerPixel,
	);
	const rgba = Buffer.alloc(width * height * 4);

	for (let i = 0, pixel = 0; pixel < width * height; pixel++, i += bytesPerPixel) {
		const target = pixel * 4;

		if (colorType === 6) {
			rgba[target] = raw[i];
			rgba[target + 1] = raw[i + 1];
			rgba[target + 2] = raw[i + 2];
			rgba[target + 3] = raw[i + 3];
		} else if (colorType === 2) {
			rgba[target] = raw[i];
			rgba[target + 1] = raw[i + 1];
			rgba[target + 2] = raw[i + 2];
			rgba[target + 3] = 255;
		} else if (colorType === 4) {
			rgba[target] = raw[i];
			rgba[target + 1] = raw[i];
			rgba[target + 2] = raw[i];
			rgba[target + 3] = raw[i + 1];
		} else {
			rgba[target] = raw[i];
			rgba[target + 1] = raw[i];
			rgba[target + 2] = raw[i];
			rgba[target + 3] = 255;
		}
	}

	return { width, height, rgba };
}

function createCrcTable() {
	const table = new Uint32Array(256);

	for (let n = 0; n < 256; n++) {
		let c = n;

		for (let k = 0; k < 8; k++) {
			c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
		}

		table[n] = c >>> 0;
	}

	return table;
}

const CRC_TABLE = createCrcTable();

function crc32(bytes) {
	let crc = 0xffffffff;

	for (let i = 0; i < bytes.length; i++) {
		crc = CRC_TABLE[(crc ^ bytes[i]) & 255] ^ (crc >>> 8);
	}

	return (crc ^ 0xffffffff) >>> 0;
}

function createPngChunk(type, data) {
	const typeBytes = Buffer.from(type, "ascii");
	const chunk = Buffer.alloc(12 + data.length);

	chunk.writeUInt32BE(data.length, 0);
	typeBytes.copy(chunk, 4);
	data.copy(chunk, 8);
	chunk.writeUInt32BE(crc32(Buffer.concat([typeBytes, data])), 8 + data.length);
	return chunk;
}

function encodePng(width, height, rgba) {
	const header = Buffer.alloc(13);
	header.writeUInt32BE(width, 0);
	header.writeUInt32BE(height, 4);
	header[8] = 8;
	header[9] = 6;
	header[10] = 0;
	header[11] = 0;
	header[12] = 0;

	const rowLength = width * 4;
	const raw = Buffer.alloc((rowLength + 1) * height);

	for (let y = 0; y < height; y++) {
		const target = y * (rowLength + 1);
		raw[target] = 0;
		rgba.copy(raw, target + 1, y * rowLength, (y + 1) * rowLength);
	}

	return Buffer.concat([
		PNG_SIGNATURE,
		createPngChunk("IHDR", header),
		createPngChunk("IDAT", zlib.deflateSync(raw, { level: 1 })),
		createPngChunk("IEND", Buffer.alloc(0)),
	]);
}

function applyPixfix(bytes) {
	const image = decodePng(bytes);
	const { width, height, rgba } = image;
	const pixelCount = width * height;
	const sourcePixels = new Int32Array(pixelCount);
	const queue = new Int32Array(pixelCount);
	const neighbors = [
		[-1, -1],
		[0, -1],
		[1, -1],
		[1, 0],
		[1, 1],
		[0, 1],
		[-1, 1],
		[-1, 0],
	];

	sourcePixels.fill(-1);

	let queueStart = 0;
	let queueEnd = 0;
	let transparentCount = 0;

	for (let y = 0; y < height; y++) {
		for (let x = 0; x < width; x++) {
			const pixel = y * width + x;
			const alpha = rgba[pixel * 4 + 3];

			if (alpha === 0) {
				transparentCount++;
				continue;
			}

			let isEdge = false;

			for (const [dx, dy] of neighbors) {
				const nx = x + dx;
				const ny = y + dy;

				if (nx < 0 || ny < 0 || nx >= width || ny >= height) {
					continue;
				}

				if (rgba[(ny * width + nx) * 4 + 3] === 0) {
					isEdge = true;
					break;
				}
			}

			if (!isEdge) {
				continue;
			}

			sourcePixels[pixel] = pixel;
			queue[queueEnd++] = pixel;
		}
	}

	if (transparentCount === 0 || queueEnd === 0) {
		return bytes;
	}

	while (queueStart < queueEnd) {
		const pixel = queue[queueStart++];
		const sourcePixel = sourcePixels[pixel];
		const x = pixel % width;
		const y = Math.floor(pixel / width);

		for (const [dx, dy] of neighbors) {
			const nx = x + dx;
			const ny = y + dy;

			if (nx < 0 || ny < 0 || nx >= width || ny >= height) {
				continue;
			}

			const next = ny * width + nx;

			if (
				rgba[next * 4 + 3] !== 0 ||
				sourcePixels[next] !== -1
			) {
				continue;
			}

			sourcePixels[next] = sourcePixel;
			queue[queueEnd++] = next;
		}
	}

	for (let pixel = 0; pixel < pixelCount; pixel++) {
		if (rgba[pixel * 4 + 3] !== 0) {
			continue;
		}

		const sourcePixel = sourcePixels[pixel];

		if (sourcePixel < 0) {
			continue;
		}

		const targetOffset = pixel * 4;
		const sourceOffset = sourcePixel * 4;

		rgba[targetOffset] = rgba[sourceOffset];
		rgba[targetOffset + 1] = rgba[sourceOffset + 1];
		rgba[targetOffset + 2] = rgba[sourceOffset + 2];
	}

	return encodePng(width, height, rgba);
}

async function uploadAsset(payload) {
	let bytes = Buffer.from(payload.data, "base64");

	if (payload.pixfix === true && payload.contentType === "image/png") {
		bytes = applyPixfix(bytes);
	}

	const form = new FormData();
	form.append("request", JSON.stringify(createAssetMetadata(payload)));
	form.append("fileContent", new Blob([bytes], { type: payload.contentType }), payload.fileName);

	const response = await fetch(ROBLOX_ASSET_URL, {
		method: "POST",
		headers: { "x-api-key": payload.apiKey },
		body: form,
	});
	const result = await parseRobloxResponse(response);
	const completedAssetId = getCompletedAssetId(result);

	if (completedAssetId) {
		return completedAssetId;
	}

	const operationId = getOperationId(result);

	if (!operationId) {
		throw new Error("Roblox did not return an upload operation ID.");
	}

	return pollOperation(operationId, payload.apiKey);
}

async function handleUpload(request, response) {
	try {
		const payload = await readJsonBody(request);
		validateUpload(payload);
		const assetId = await uploadAsset(payload);
		console.log(
			`${payload.fileName} uploaded as ${assetId}${payload.pixfix === true && payload.contentType === "image/png" ? " [Pixfix]" : ""}`,
		);
		sendJson(response, 200, { assetId });
	} catch (error) {
		sendJson(response, 400, { error: error.message || "Image upload failed." });
	}
}

function handleRequest(request, response) {
	if (request.method === "OPTIONS") {
		response.writeHead(204, getCorsHeaders());
		response.end();
		return;
	}

	if (request.method === "GET" && request.url === "/health") {
		sendJson(response, 200, {
			ok: true,
			version: BRIDGE_VERSION,
			assetType: "Image",
			pixfix: true,
		});
		return;
	}

	if (request.method === "POST" && request.url === "/upload") {
		handleUpload(request, response);
		return;
	}

	sendJson(response, 404, { error: "Route not found." });
}

const server = http.createServer(handleRequest);

server.listen(PORT, HOST, () => {
	console.log(`RoImport server running at http://localhost:${PORT}`);
});
