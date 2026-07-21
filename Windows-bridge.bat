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

if not "%EXIT_CODE%"=="0" echo The server ha stopped with exit code %EXIT_CODE%.
pause
exit /b %EXIT_CODE%

::ROIMPORT_BRIDGE
const http = require("node:http");

const HOST = "127.0.0.1";
const PORT = 27123;
const BRIDGE_VERSION = 2;
const MAX_BODY_SIZE = 32 * 1024 * 1024;
const ROBLOX_ASSET_URL = "https://apis.roblox.com/assets/v1/assets";
const ROBLOX_OPERATION_URL = "https://apis.roblox.com/assets/v1/operations";

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

async function uploadAsset(payload) {
	const bytes = Buffer.from(payload.data, "base64");
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
		console.log(`${payload.fileName} uploaded as ${assetId}`);
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
		sendJson(response, 200, { ok: true, version: BRIDGE_VERSION, assetType: "Image" });
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
