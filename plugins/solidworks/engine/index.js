const fs = require('fs');

// Argumente von C# entgegennehmen (Input- und Output-Dateipfade)
const inputFile = process.argv[2];
const outputFile = process.argv[3];

if (!inputFile || !outputFile) {
    console.error("Fehlende Dateipfade.");
    process.exit(1);
}

try {
    // 1. Daten aus SolidWorks einlesen
    const inputRaw = fs.readFileSync(inputFile, 'utf8');
    const inputData = JSON.parse(inputRaw);

    console.log(`Daten erhalten! Modell-Dreiecke empfangen.`);

    // 2. Dummy-Ergebnis für den ersten Verbindungstest zurückliefern
    const result = {
        triangles: inputData.triangles, // Spiegelt die Geometrie als Bestätigung
        offset: [0, 0, 0]
    };

    fs.writeFileSync(outputFile, JSON.stringify(result, null, 2), 'utf8');
    process.exit(0);
} catch (err) {
    console.error("Fehler in der Engine: " + err.message);
    process.exit(1);
}