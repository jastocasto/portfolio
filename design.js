document.addEventListener('DOMContentLoaded', () => {
    const imageUrls = [
        'images/face1.png',
        'images/face2.png',
        'images/face3.png',
        'images/face4.png',
        'images/face5.png'
    ];

    const minThreshold = 30;
    const maxThreshold = 240;
    const thresholdIncrement = 30;
    let threshold = 240;
    const maxVisible = 5;
    const images = [];
    let currentIndex = 0;
    let lastMoveX = 0;
    let lastMoveY = 0;
    let initialMove = 250;
    let moved = false;

    const container = document.createElement('div');
    container.style.position = 'absolute';
    container.style.pointerEvents = 'none';
    document.body.appendChild(container);

    const updateCounter = () => {
        const counter = document.getElementById('image-counter');
        counter.textContent = `${(currentIndex % imageUrls.length) + 1}/${imageUrls.length}`;
    };

    const createImageElement = (url) => {
        const img = document.createElement('img');
        img.src = url;
        img.style.position = 'absolute';
        img.style.transition = 'opacity .1s';
        img.style.opacity = '0'; // Start with opacity 0 to hide until fully loaded
        img.style.zIndex = '900'; // Ensure images are above other content
        img.onload = () => {
            img.style.left = `${lastMoveX - img.clientWidth / 2}px`; // Center the image
            img.style.top = `${lastMoveY - img.clientHeight / 2}px`; // Center the image
            img.style.opacity = '1'; // Fade in the image after positioning
        };
        container.appendChild(img);
        return img;
    };

    const updateImages = (x, y) => {
        if (images.length >= maxVisible) {
            const oldestImage = images.shift();
            oldestImage.style.opacity = '0';
            setTimeout(() => container.removeChild(oldestImage), 2000); // Shorter delay before removal
        }

        const img = createImageElement(imageUrls[currentIndex]);
        images.push(img);

        currentIndex = (currentIndex + 1) % imageUrls.length;
        updateCounter();
    };

    const updateThresholdDisplay = () => {
        const thresholdCounter = document.getElementById('threshold-counter');
        thresholdCounter.textContent = threshold;
    };

    const handleMouseMove = (event) => {
        const x = event.clientX;
        const y = event.clientY;

        // Only start showing images after the mouse has moved at least 300px initially
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
                updateImages(x, y);
            }
        }
    };

    document.addEventListener('mousemove', handleMouseMove);

    // Initialize counter
    updateCounter();

    // Initialize threshold counter
    updateThresholdDisplay();

    // Add threshold adjuster button functionality
    document.getElementById('decrease-threshold').addEventListener('click', () => {
        if (threshold > minThreshold) {
            threshold -= thresholdIncrement;
            updateThresholdDisplay();
        }
    });

    document.getElementById('increase-threshold').addEventListener('click', () => {
        if (threshold < maxThreshold) {
            threshold += thresholdIncrement;
            updateThresholdDisplay();
        }
    });
});
