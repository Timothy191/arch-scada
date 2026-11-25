#!/usr/bin/env node
import fs from 'node:fs/promises';
import path from 'node:path';
import { NodeIO } from '@gltf-transform/core';
import { dedup, flatten, join, prune, reorder } from '@gltf-transform/functions';
import { MeshoptEncoder } from 'meshoptimizer';

const MIN_GATE3_BYTES = 8192;
const GLTF_MAGIC = 0x46546c67;

function countSceneTriangles(doc) {
    let total = 0;
    const root = doc.getRoot();
    for (const node of root.listNodes()) {
        const mesh = node.getMesh();
        if (!mesh) continue;
        for (const prim of mesh.listPrimitives()) {
            const mode = prim.getMode();
            if (mode !== 4) continue; // TRIANGLES
            const indices = prim.getIndices();
            if (indices) {
                total += Math.floor(indices.getCount() / 3);
            } else {
                const pos = prim.getAttribute('POSITION');
                if (pos) total += Math.floor(pos.getCount() / 3);
            }
        }
    }
    return total;
}

function countPrimitives(doc) {
    let count = 0;
    for (const mesh of doc.getRoot().listMeshes()) {
        count += mesh.listPrimitives().length;
    }
    return count;
}

function isValidGlbHeader(buf) {
    if (buf.byteLength < 12) return false;
    const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
    const magic = view.getUint32(0, true);
    const version = view.getUint32(4, true);
    const length = view.getUint32(8, true);
    return magic === GLTF_MAGIC && version === 2 && length === buf.byteLength;
}

async function main() {
    const targetDir = path.resolve(process.argv[2] || './Exported');
    await MeshoptEncoder.ready;

    const io = new NodeIO();
    const entries = (await fs.readdir(targetDir)).filter(f => f.toLowerCase().endsWith('.glb')).sort();

    if (entries.length === 0) {
        console.error(`No .glb files found in ${targetDir}`);
        process.exit(1);
    }

    let totalBefore = 0;
    let totalAfter = 0;

    for (const file of entries) {
        const fullPath = path.join(targetDir, file);
        const origBytes = await fs.readFile(fullPath);
        totalBefore += origBytes.byteLength;

        const doc = await io.readBinary(new Uint8Array(origBytes));
        const triBefore = countSceneTriangles(doc);
        const primBefore = countPrimitives(doc);

        await doc.transform(
            dedup(),
            flatten(),
            join(),
            prune(),
            reorder({ encoder: MeshoptEncoder })
        );

        const triAfter = countSceneTriangles(doc);
        const primAfter = countPrimitives(doc);
        const outBytes = await io.writeBinary(doc);

        if (triAfter !== triBefore) {
            console.warn(`[SKIP] ${file}: triangle count changed (${triBefore} -> ${triAfter}); keeping original.`);
            totalAfter += origBytes.byteLength;
            continue;
        }
        if (!isValidGlbHeader(outBytes)) {
            console.warn(`[SKIP] ${file}: invalid glTF v2 header; keeping original.`);
            totalAfter += origBytes.byteLength;
            continue;
        }
        if (outBytes.byteLength <= MIN_GATE3_BYTES) {
            console.warn(`[SKIP] ${file}: optimized size ${outBytes.byteLength} B <= ${MIN_GATE3_BYTES} B (Gate 3 minimum); keeping original (${origBytes.byteLength} B).`);
            totalAfter += origBytes.byteLength;
            continue;
        }

        await fs.writeFile(fullPath, outBytes);
        totalAfter += outBytes.byteLength;
        const pct = (((origBytes.byteLength - outBytes.byteLength) / origBytes.byteLength) * 100).toFixed(1);
        console.log(
            `[OK] ${file}: ${(origBytes.byteLength / 1024).toFixed(1)} KB -> ${(outBytes.byteLength / 1024).toFixed(1)} KB (-${pct}%) | tris=${triAfter} | prims=${primBefore}->${primAfter}`
        );
    }

    const totalPct = (((totalBefore - totalAfter) / totalBefore) * 100).toFixed(1);
    console.log(
        `Total across ${entries.length} GLBs: ${(totalBefore / (1024 * 1024)).toFixed(2)} MiB -> ${(totalAfter / (1024 * 1024)).toFixed(2)} MiB (-${totalPct}%)`
    );
}

main().catch(err => {
    console.error(err);
    process.exit(1);
});
