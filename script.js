document.addEventListener('DOMContentLoaded', () => {
    const imageUrls = [
        'images/arch1.png',
        'images/arch2.png',
        'images/arch3.png',
        'images/arch4.png',
        'images/arch5.png',
        'images/arch6.png',
        'images/arch7.png',
        'images/arch8.png',
        'images/arch9.png',
        'images/arch10.png',
        'images/arch11.png',
        'images/arch12.png',
        'images/arch13.png',
        'images/arch14.png',
        'images/arch15.png',
    ];

    const minThreshold = 30;
    const maxThreshold = 240;
    const thresholdIncrement = 30;
    let threshold = 90;
    const maxVisible = 10;
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
        img.style.transition = 'opacity .01s, width .01s, height .01s';
        img.style.opacity = '0';
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
            enlargeImage(img);
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
        thresholdCounter.textContent = threshold.toString().padStart(3, '0');
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
