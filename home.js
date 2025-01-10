document.addEventListener('DOMContentLoaded', () => {
    const wordLinks = [
        { word: 'ARCHITECTURE', url: 'architecture.html' },
        { word: 'DESIGN', url: 'design.html' },
        { word: 'CONCEPT', url: 'concept.html' },
        { word: 'INFO', url: 'info.html' }
    ];

    const minThreshold = 30;
    const maxThreshold = 240;
    const thresholdIncrement = 30;
    let threshold = 90; // Initial threshold value
    const maxVisible = 50;
    const words = [];
    let currentIndex = 0;
    let lastMoveX = 0;
    let lastMoveY = 0;
    let initialMove = 200;
    let moved = false;

    const container = document.createElement('div');
    container.style.position = 'absolute';
    container.style.pointerEvents = 'none';
    document.body.appendChild(container);

    const updateCounter = () => {
        const counter = document.getElementById('image-counter');
        counter.textContent = `${(currentIndex % wordLinks.length) + 1}/${wordLinks.length}`;
    };

    const createWordElement = (word, url) => {
        const anchor = document.createElement('a');
        anchor.textContent = word;
        anchor.href = url;
        anchor.className = 'word-element';
        anchor.style.opacity = '0'; // Start with opacity 0 to hide until fully positioned
        anchor.style.zIndex = '900'; // Ensure words are above other content
        anchor.style.pointerEvents = 'auto'; // Enable click events

        container.appendChild(anchor);
        return anchor;
    };

    const updateWords = (x, y) => {
        if (words.length >= maxVisible) {
            const oldestWord = words.shift();
            oldestWord.style.opacity = '0';
            setTimeout(() => container.removeChild(oldestWord), 500); // Adjusted for quicker removal
        }

        const wordElement = createWordElement(wordLinks[currentIndex].word, wordLinks[currentIndex].url);
        wordElement.style.left = `${x}px`; // Adjust to center the word relative to the cursor
        wordElement.style.top = `${y}px`; // Adjust to center the word relative to the cursor
        wordElement.style.opacity = '1';

        words.push(wordElement);
        currentIndex = (currentIndex + 1) % wordLinks.length;
        updateCounter();
    };

    const handleMouseMove = (event) => {
        const x = event.clientX;
        const y = event.clientY;

        if (!moved) {
            if (Math.abs(x - lastMoveX) >= initialMove || Math.abs(y - lastMoveY) >= initialMove) {
                moved = true;
                lastMoveX = x;
                lastMoveY = y;
            }
        } else {
            if (Math.abs(x - lastMoveX) >= threshold || Math.abs(y - lastMoveY) >= threshold) {
                lastMoveX = x;
                lastMoveY = y;
                updateWords(x, y);
            }
        }
    };

    document.addEventListener('mousemove', handleMouseMove);

    // Initialize counter
    updateCounter();

    // Function to update the displayed threshold
    const updateThresholdDisplay = () => {
        const thresholdCounter = document.getElementById('threshold-counter');
        thresholdCounter.textContent = threshold; // Update the displayed threshold value
    };

    // Threshold adjuster button functionality
    document.getElementById('decrease-threshold').addEventListener('click', () => {
        if (threshold > minThreshold) {
            threshold -= thresholdIncrement;
            updateThresholdDisplay(); // Update display after changing threshold
        }
    });

    document.getElementById('increase-threshold').addEventListener('click', () => {
        if (threshold < maxThreshold) {
            threshold += thresholdIncrement;
            updateThresholdDisplay(); // Update display after changing threshold
        }
    });

    // Initialize threshold counter display
    updateThresholdDisplay();
});