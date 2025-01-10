document.addEventListener('DOMContentLoaded', () => {
    const imageUrls = [
        'https://i.pinimg.com/564x/3c/e9/8d/3ce98d3ab8ce1a6282dfec4f09cee306.jpg',
        'https://i.pinimg.com/564x/8c/06/e2/8c06e2431bd89d85c8136eda75826b0a.jpg',
        'https://i.pinimg.com/736x/3f/58/5c/3f585c3a7d09c3e2885950b4eac3fbd7.jpg'
    ];

    const minThreshold = 30;
    const maxThreshold = 240;
    const thresholdIncrement = 30;
    let threshold = 90;
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
        if (counter) {
            counter.textContent = `${(currentIndex % imageUrls.length) + 1}/${imageUrls.length}`;
        }
    };

    const createImageElement = (url) => {
        const img = document.createElement('img');
        img.src = url;
        img.style.position = 'absolute';
        img.style.transition = 'opacity .3s, transform .3s'; // Include transform for smooth scaling
        img.style.opacity = '0'; // Initially hide the image
        img.style.zIndex = '900';

        img.style.objectFit = 'cover';
        img.onload = () => {
            img.style.left = `${lastMoveX - 100}px`;
            img.style.top = `${lastMoveY - 100}px`;
            img.style.opacity = '1';
        };
        container.appendChild(img);

        img.addEventListener('click', (event) => {
            event.stopPropagation();
            if (img.classList.contains('enlarged')) {
                img.classList.remove('enlarged');
            } else {
                img.classList.add('enlarged');
            }
        });

        return img;
    };

    const updateImages = (x, y) => {
        if (images.length >= maxVisible) {
            const oldestImage = images.shift();
            oldestImage.style.opacity = '0';
            setTimeout(() => container.removeChild(oldestImage), 2000);
        }

        const img = createImageElement(imageUrls[currentIndex]);
        images.push(img);

        currentIndex = (currentIndex + 1) % imageUrls.length;
        updateCounter();
    };

    const updateThresholdDisplay = () => {
        const thresholdCounter = document.getElementById('threshold-counter');
        if (thresholdCounter) {
            thresholdCounter.textContent = threshold.toString().padStart(3, '0');
        }
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
                updateImages(x, y);
            }
        }
    };

    document.addEventListener('mousemove', handleMouseMove);

    // Initialize UI elements
    updateCounter();
    updateThresholdDisplay();

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
